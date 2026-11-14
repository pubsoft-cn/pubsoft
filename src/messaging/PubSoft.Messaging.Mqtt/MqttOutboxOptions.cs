namespace PubSoft.Messaging.Mqtt;

/// <summary>Options for the MQTT outbox dispatcher.</summary>
public sealed class MqttOutboxOptions
{
    /// <summary>Broker URI (mqtt://host:port or mqtts://host:port).</summary>
    public string BrokerUri { get; set; } = "mqtt://localhost:1883";
    /// <summary>MQTT client identifier.</summary>
    public string ClientId { get; set; } = "pubsoft-outbox";
    /// <summary>Broker username.</summary>
    public string? Username { get; set; }
    /// <summary>Broker password.</summary>
    public string? Password { get; set; }
    /// <summary>Topic prefix -- events publish to {TopicPrefix}{EventType}.</summary>
    public string TopicPrefix { get; set; } = "outbox/";
    /// <summary>Publish QoS level. Default AtLeastOnce matches outbox at-least-once + inbox dedup semantics.</summary>
    public int QualityOfService { get; set; } = 1;
    /// <summary>Persistent session (clean_start=0) for offline-queue durability.</summary>
    public bool PersistentSession { get; set; } = true;
    /// <summary>Maximum payload bytes allowed by the broker; larger payloads must use claim-check upstream.</summary>
    public int MaxPayloadBytes { get; set; } = 64 * 1024;
    /// <summary>Publish + PUBACK wait timeout.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal MqttOutboxOptions CloneAndValidate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BrokerUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(TopicPrefix);
        if (QualityOfService is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(QualityOfService));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPayloadBytes);
        return (MqttOutboxOptions)MemberwiseClone();
    }
}
