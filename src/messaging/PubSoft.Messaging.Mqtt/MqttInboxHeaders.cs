using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Mqtt;


/// <summary>MQTT5 user-property headers carried on published outbox messages.</summary>
public static class MqttInboxHeaders
{
    /// <summary>OutboxMessage.Id -- consumer-side dedup key (InboxReceipt.MessageId).</summary>
    public const string OutboxMessageId = OutboxHeaders.OutboxMessageId;
    /// <summary>OutboxMessage.AggregateKey -- optional ordering/ownership hint.</summary>
    public const string OutboxAggregateKey = OutboxHeaders.OutboxAggregateKey;
}
