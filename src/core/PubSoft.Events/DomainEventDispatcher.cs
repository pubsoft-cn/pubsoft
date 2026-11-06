using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Events;


public sealed class DomainEventDispatcher : IDomainEventDispatcher, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainEventDispatcher>? _logger;
    private readonly Channel<IDomainEvent> _channel;
    private readonly object _handlerGate = new();
    private readonly Dictionary<Type, List<Func<IDomainEvent, CancellationToken, Task>>> _manualHandlers = [];
    private readonly ConcurrentDictionary<Type, MethodInfo> _handlerMethods = new();

    public DomainEventDispatcher(
        IServiceScopeFactory scopeFactory,
        DomainEventOptions? options = null,
        ILogger<DomainEventDispatcher>? logger = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger;
        var capacity = (options ?? new DomainEventOptions()).ValidateAndGetCapacity();
        _channel = Channel.CreateBounded<IDomainEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    internal ChannelReader<IDomainEvent> Reader => _channel.Reader;

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        await DispatchCoreAsync(typeof(TEvent), @event, cancellationToken, isolateHandlerFailures: false)
            .ConfigureAwait(false);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        await _channel.Writer.WriteAsync(@event, cancellationToken).ConfigureAwait(false);
    }

    public void RegisterHandler<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlerGate)
        {
            if (!_manualHandlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = [];
                _manualHandlers.Add(typeof(TEvent), handlers);
            }

            handlers.Add((@event, cancellationToken) => handler((TEvent)@event, cancellationToken));
        }
    }

    public void Dispose() => CompleteWriter();

    internal void CompleteWriter() => _channel.Writer.TryComplete();

    internal async Task DispatchPublishedAsync(IDomainEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await DispatchCoreAsync(@event.GetType(), @event, cancellationToken, isolateHandlerFailures: true)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogHandlerFailure("EventDispatch", @event.GetType(), exception);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "DI-registered handlers are resolved by runtime event type and invoked through cached MethodInfo; AOT hosts should register delegate handlers via RegisterHandler<TEvent>.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "DI-registered handlers are resolved via closed generic service types built at runtime; AOT hosts should register delegate handlers via RegisterHandler<TEvent>.")]
    private async Task DispatchCoreAsync(
        Type eventType,
        IDomainEvent @event,
        CancellationToken cancellationToken,
        bool isolateHandlerFailures)
    {
        Func<IDomainEvent, CancellationToken, Task>[] manualHandlers;
        lock (_handlerGate)
            manualHandlers = _manualHandlers.TryGetValue(eventType, out var handlers) ? handlers.ToArray() : [];

        foreach (var handler in manualHandlers)
        {
            try
            {
                await handler(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (!isolateHandlerFailures)
                    throw;
                LogHandlerFailure(handler.Method.DeclaringType?.Name ?? "ManualHandler", eventType, exception);
            }
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var closedHandlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var method = _handlerMethods.GetOrAdd(closedHandlerType, type =>
            type.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
            ?? throw new InvalidOperationException($"Handler method not found for {type.Name}."));

        foreach (var handler in scope.ServiceProvider.GetServices(closedHandlerType))
        {
            if (handler is null)
                continue;

            try
            {
                Task? handlingTask;
                try
                {
                    handlingTask = method.Invoke(handler, [@event, cancellationToken]) as Task;
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }

                if (handlingTask is not null)
                    await handlingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (!isolateHandlerFailures)
                    throw;
                LogHandlerFailure(handler.GetType().Name, eventType, exception);
            }
        }
    }

    private void LogHandlerFailure(string handlerName, Type eventType, Exception exception)
        => _logger?.LogError(
            "Domain event handler {HandlerType} failed for {EventType} ({ExceptionType}).",
            handlerName,
            eventType.Name,
            exception.GetType().Name);
}
