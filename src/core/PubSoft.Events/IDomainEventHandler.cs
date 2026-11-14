namespace PubSoft.Events;


/// <summary>Handles one event type.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>Handles the event.</summary>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
