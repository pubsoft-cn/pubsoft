namespace PubSoft.Messaging.Amqp;

/// <summary>Options for the AMQP (RabbitMQ) outbox dispatcher.</summary>
public sealed class AmqpOutboxOptions
{
    /// <summary>Broker URI (amqp://user:pass@host:port/vhost or amqps:// for TLS).</summary>
    public string BrokerUri { get; set; } = "amqp://guest:guest@localhost:5672/";
    /// <summary>Exchange events publish to; declared idempotently as durable.</summary>
    public string Exchange { get; set; } = "pubsoft.outbox";
    /// <summary>Exchange type used when declaring <see cref="Exchange"/>.</summary>
    public string ExchangeType { get; set; } = RabbitMQ.Client.ExchangeType.Topic;
    /// <summary>Routing-key prefix -- events publish to {RoutingKeyPrefix}{EventType}.</summary>
    public string RoutingKeyPrefix { get; set; } = "outbox.";
    /// <summary>
    /// Publish with the mandatory flag so the broker returns unroutable messages instead of
    /// dropping them; a returned message fails the dispatch and the outbox retries.
    /// </summary>
    public bool Mandatory { get; set; } = true;
    /// <summary>Maximum payload bytes allowed per message; larger payloads must use claim-check upstream.</summary>
    public int MaxPayloadBytes { get; set; } = 1_048_576;
    /// <summary>Publish + broker-confirm wait timeout.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal AmqpOutboxOptions CloneAndValidate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BrokerUri);
        var uri = new Uri(BrokerUri, UriKind.Absolute);
        if (uri.Scheme is not ("amqp" or "amqps"))
            throw new ArgumentException("BrokerUri scheme must be amqp:// or amqps://.", nameof(BrokerUri));
        ArgumentException.ThrowIfNullOrWhiteSpace(Exchange);
        ArgumentNullException.ThrowIfNull(RoutingKeyPrefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPayloadBytes);
        if (PublishTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PublishTimeout), "Publish timeout must be positive.");
        return (AmqpOutboxOptions)MemberwiseClone();
    }
}
