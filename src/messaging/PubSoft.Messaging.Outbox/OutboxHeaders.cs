namespace PubSoft.Messaging.Outbox;


/// <summary>
/// Transport-agnostic message headers carried on dispatched outbox messages.
/// Every dispatcher implementation (MQTT, AMQP, ...) must attach these so consumer-side
/// InboxReceipt dedup works identically regardless of transport.
/// </summary>
public static class OutboxHeaders
{
    /// <summary>OutboxMessage.Id -- consumer-side dedup key (InboxReceipt.MessageId).</summary>
    public const string OutboxMessageId = "x-outbox-id";
    /// <summary>OutboxMessage.AggregateKey -- optional ordering/ownership hint.</summary>
    public const string OutboxAggregateKey = "x-outbox-aggkey";
    /// <summary>OutboxMessage.CorrelationId -- trace correlation carried across the bus hop.</summary>
    public const string OutboxCorrelationId = "x-outbox-correlation-id";
}
