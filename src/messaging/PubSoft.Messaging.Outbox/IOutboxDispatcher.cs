namespace PubSoft.Messaging.Outbox;


/// <summary>Transport seam: delivers a claimed outbox message to the bus.</summary>
public interface IOutboxDispatcher
{
    /// <summary>Dispatches the message; throwing schedules a retry.</summary>
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
