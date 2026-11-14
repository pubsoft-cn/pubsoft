namespace PubSoft.Messaging.Mqtt;

/// <summary>Options for the MQTT inbox subscriber (consumer side of the outbox transport).</summary>
public sealed class MqttInboxOptions
{
    /// <summary>Broker URI (mqtt://host:port or mqtts://host:port).</summary>
    public string BrokerUri { get; set; } = "mqtt://localhost:1883";
    /// <summary>MQTT client identifier -- must be unique per instance.</summary>
    public string ClientId { get; set; } = "pubsoft-inbox";
    /// <summary>Broker username.</summary>
    public string? Username { get; set; }
    /// <summary>Broker password.</summary>
    public string? Password { get; set; }
    /// <summary>Topic filter, e.g. "ext/outbox/#".</summary>
    public string TopicFilter { get; set; } = "outbox/#";
    /// <summary>
    /// Shared-subscription group name. When set the filter becomes
    /// $share/{ShareGroup}/{TopicFilter} -- instances in the same group
    /// load-balance deliveries instead of each receiving every message.
    /// </summary>
    public string? ShareGroup { get; set; } = "outbox-workers";
    /// <summary>Consumer identity for InboxReceipt dedup (per logical consumer, not per instance).</summary>
    public string ConsumerName { get; set; } = "default";
    /// <summary>Subscription QoS -- AtLeastOnce pairs with inbox dedup.</summary>
    public int QualityOfService { get; set; } = 1;
    /// <summary>Persistent session (clean_start=0) so the broker queues messages while offline.</summary>
    public bool PersistentSession { get; set; } = true;
    /// <summary>
    /// MQTT5 Session Expiry Interval in seconds. Required for offline-queue durability:
    /// without it the broker destroys the session on disconnect even with clean_start=0.
    /// Default 86400 (24h) -- applied only when PersistentSession is true; set 0 for
    /// "expire at network disconnect" semantics.
    /// </summary>
    public uint SessionExpirySeconds { get; set; } = 86_400;
    /// <summary>Reconnect delay after a broker disconnect.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Prefix stripped from the topic to derive the event type; empty -> full topic used.</summary>
    public string EventTypePrefix { get; set; } = "outbox/";

    internal MqttInboxOptions CloneAndValidate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BrokerUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(TopicFilter);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConsumerName);
        if (QualityOfService is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(QualityOfService));
        return (MqttInboxOptions)MemberwiseClone();
    }
}
