namespace PubSoft.Messaging.Amqp;

/// <summary>Options for the AMQP (RabbitMQ) inbox subscriber (consumer side of the outbox transport).</summary>
public sealed class AmqpInboxOptions
{
    /// <summary>Broker URI (amqp://user:pass@host:port/vhost or amqps:// for TLS).</summary>
    public string BrokerUri { get; set; } = "amqp://guest:guest@localhost:5672/";
    /// <summary>Durable queue the subscriber consumes; declared when <see cref="DeclareTopology"/> is on.</summary>
    public string Queue { get; set; } = "pubsoft.outbox.inbox";
    /// <summary>Exchange the queue binds to; declared when <see cref="DeclareTopology"/> is on.</summary>
    public string Exchange { get; set; } = "pubsoft.outbox";
    /// <summary>Exchange type used when declaring <see cref="Exchange"/>.</summary>
    public string ExchangeType { get; set; } = RabbitMQ.Client.ExchangeType.Topic;
    /// <summary>Binding key -- the default "outbox.#" matches the outbox dispatcher's routing keys.</summary>
    public string BindingKey { get; set; } = "outbox.#";
    /// <summary>
    /// Whether the subscriber declares exchange/queue/binding itself. Disable when broker
    /// topology is managed externally; the queue must then already exist and be bound.
    /// </summary>
    public bool DeclareTopology { get; set; } = true;
    /// <summary>Consumer identity for InboxReceipt dedup (per logical consumer, not per instance).</summary>
    public string ConsumerName { get; set; } = "default";
    /// <summary>Unacked deliveries the broker may push at once (prefetch).</summary>
    public ushort PrefetchCount { get; set; } = 16;
    /// <summary>Reconnect delay after a broker disconnect or channel failure.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Prefix stripped from the routing key to derive the event type; empty -> full key used.</summary>
    public string EventTypePrefix { get; set; } = "outbox.";

    internal AmqpInboxOptions CloneAndValidate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BrokerUri);
        var uri = new Uri(BrokerUri, UriKind.Absolute);
        if (uri.Scheme is not ("amqp" or "amqps"))
            throw new ArgumentException("BrokerUri scheme must be amqp:// or amqps://.", nameof(BrokerUri));
        ArgumentException.ThrowIfNullOrWhiteSpace(Queue);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConsumerName);
        if (DeclareTopology)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Exchange);
            ArgumentException.ThrowIfNullOrWhiteSpace(BindingKey);
        }
        ArgumentOutOfRangeException.ThrowIfEqual(PrefetchCount, (ushort)0);
        if (ReconnectDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReconnectDelay), "Reconnect delay must not be negative.");
        ArgumentNullException.ThrowIfNull(EventTypePrefix);
        return (AmqpInboxOptions)MemberwiseClone();
    }
}
