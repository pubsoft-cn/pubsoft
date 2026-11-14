namespace PubSoft.Events;


/// <summary>Dispatches domain events to their handlers.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Invokes handlers synchronously; handler exceptions propagate.</summary>
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>Queues the event for background fan-out; handler failures are isolated.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
