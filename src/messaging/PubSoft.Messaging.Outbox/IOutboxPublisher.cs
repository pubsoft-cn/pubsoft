namespace PubSoft.Messaging.Outbox;


/// <summary>Enqueues an event into the transactional outbox inside the ambient DbContext transaction.</summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Writes the event; returns the Snowflake message id. Correlation falls back to the ambient Activity trace id.
    /// <paramref name="eventType"/> is required and must be the stable, versioned contract key used in the durable row
    /// and transport routing; CLR type-name inference is deliberately not supported.
    /// </summary>
    Task<long> PublishAsync<TEvent>(
        TEvent @event,
        string eventType,
        string? targetModule = null,
        string? aggregateKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
