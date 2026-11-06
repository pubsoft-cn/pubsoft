using System.Text;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Mqtt;


/// <summary>MQTT5 user-property headers carried on published outbox messages.</summary>
public static class MqttInboxHeaders
{
    /// <summary>OutboxMessage.Id — consumer-side dedup key (InboxReceipt.MessageId).</summary>
    public const string OutboxMessageId = "x-outbox-id";
    /// <summary>OutboxMessage.AggregateKey — optional ordering/ownership hint.</summary>
    public const string OutboxAggregateKey = "x-outbox-aggkey";
}
