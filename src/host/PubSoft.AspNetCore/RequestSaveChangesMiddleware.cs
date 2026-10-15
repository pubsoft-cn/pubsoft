using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;

/// <summary>Collects persistence callbacks and executes them once for a successful request.</summary>
/// <remarks>Callbacks run sequentially and are not an atomic transaction across multiple stores.</remarks>
public sealed class PubSoftRequestSaveChanges : ISaveChangesBoundary
{
    private readonly object _sync = new();
    private readonly List<Func<CancellationToken, Task>> _callbacks = [];
    private bool _saveStarted;
    private bool _serviceParticipantsRegistered;
    private bool _isActive;
    private Task? _saveTask;

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <inheritdoc />
    public void Activate() => _isActive = true;

    public void Register(Func<CancellationToken, Task> saveCallback)
    {
        ArgumentNullException.ThrowIfNull(saveCallback);
        lock (_sync)
        {
            if (_saveStarted)
                throw new InvalidOperationException("Save callbacks cannot be registered after saving has started.");

            _callbacks.Add(saveCallback);
        }
    }

    /// <summary>
    /// Enrolls each registered <see cref="IPubSoftSaveParticipant"/> exactly once, after any explicit
    /// callbacks. Resolving participants is left to the caller; a no-op if already enrolled or saving
    /// has already started.
    /// </summary>
    public void RegisterServiceParticipants(IEnumerable<IPubSoftSaveParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        lock (_sync)
        {
            if (_serviceParticipantsRegistered || _saveStarted)
                return;

            _serviceParticipantsRegistered = true;
            foreach (var participant in participants)
                _callbacks.Add(participant.SaveChangesAsync);
        }
    }

    public Task SaveAllAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task>[] callbacks;
        TaskCompletionSource<bool> completion;
        lock (_sync)
        {
            if (_saveTask is not null)
                return _saveTask;

            _saveStarted = true;
            callbacks = _callbacks.ToArray();
            _callbacks.Clear();
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _saveTask = completion.Task;
        }

        _ = CompleteSaveAsync(callbacks, cancellationToken, completion);
        return completion.Task;
    }

    private static async Task CompleteSaveAsync(
        IReadOnlyList<Func<CancellationToken, Task>> callbacks,
        CancellationToken cancellationToken,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            foreach (var callback in callbacks)
                await callback(cancellationToken).ConfigureAwait(false);

            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}

public sealed class PubSoftRequestSaveChangesMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(HttpContext context)
    {
        var saveChanges = context.RequestServices.GetService<PubSoftRequestSaveChanges>();
        if (saveChanges is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        saveChanges.Activate();

        context.Response.OnStarting(
            static async state =>
            {
                var (httpContext, coordinator) = ((HttpContext, PubSoftRequestSaveChanges))state;
                if (httpContext.Response.StatusCode < StatusCodes.Status400BadRequest)
                {
                    coordinator.RegisterServiceParticipants(
                        httpContext.RequestServices.GetServices<IPubSoftSaveParticipant>());
                    await coordinator.SaveAllAsync(httpContext.RequestAborted).ConfigureAwait(false);
                }
            },
            (context, saveChanges));

        await _next(context).ConfigureAwait(false);

        if (!context.Response.HasStarted && context.Response.StatusCode < StatusCodes.Status400BadRequest)
        {
            saveChanges.RegisterServiceParticipants(
                context.RequestServices.GetServices<IPubSoftSaveParticipant>());
            await saveChanges.SaveAllAsync(context.RequestAborted).ConfigureAwait(false);
        }
    }
}

public static class PubSoftRequestSaveChangesExtensions
{
    public static IServiceCollection AddPubSoftRequestSaveChanges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<PubSoftRequestSaveChanges>();
        services.AddScoped<ISaveChangesBoundary>(provider => provider.GetRequiredService<PubSoftRequestSaveChanges>());
        return services;
    }

    public static IApplicationBuilder UsePubSoftRequestSaveChanges(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRequestSaveChangesMiddleware>();
    }
}