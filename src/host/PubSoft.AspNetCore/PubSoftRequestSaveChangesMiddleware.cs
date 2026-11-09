using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;


/// <summary>Commits the ambient unit of work at request end.</summary>
public sealed class PubSoftRequestSaveChangesMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <summary>Forwards then saves changes.</summary>
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
