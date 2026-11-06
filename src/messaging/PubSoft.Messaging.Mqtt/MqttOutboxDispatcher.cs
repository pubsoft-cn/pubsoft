using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Mqtt;

/// <summary>Options for the MQTT outbox dispatcher.</summary>
public sealed class MqttOutboxOptions
{
    /// <summary>Broker URI (mqtt://host:port or mqtts://host:port).</summary>
    public string BrokerUri { get; set; } = "mqtt://localhost:1883";
    /// <summary>MQTT client identifier.</summary>
    public string ClientId { get; set; } = "pubsoft-outbox";
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Topic prefix — events publish to {TopicPrefix}{EventType}.</summary>
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

/// <summary>
/// Outbox dispatcher that publishes committed outbox messages to an MQTT broker.
/// Delivery semantics: QoS1 PUBACK confirms broker acceptance before the outbox
/// marks the message delivered; dedup on the consumer side stays the InboxReceipt contract.
/// </summary>
public sealed class MqttOutboxDispatcher : IOutboxDispatcher, IAsyncDisposable
{
    private readonly MqttOutboxOptions _options;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private volatile bool _connected;

    public MqttOutboxDispatcher(MqttOutboxOptions options)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).CloneAndValidate();
        _client = new MqttClientFactory().CreateMqttClient();
    }

    internal IMqttClient Client => _client; // test seam

    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = Encoding.UTF8.GetBytes(message.PayloadJson);
        if (payload.Length > _options.MaxPayloadBytes)
            throw new InvalidOperationException(
                $"OutboxMessage {message.Id} payload ({payload.Length}B) exceeds broker limit ({_options.MaxPayloadBytes}B); use claim-check for large events.");

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var mqtt = new MqttApplicationMessageBuilder()
            .WithTopic(_options.TopicPrefix + message.EventType)
            .WithPayload(payload)
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)_options.QualityOfService)
            .Build();
        // dedup identity rides as MQTT5 user property — consumers key InboxReceipt on it
        mqtt.UserProperties = [new MqttUserProperty(MqttInboxHeaders.OutboxMessageId, message.Id.ToString("N"))];
        if (message.AggregateKey is { } agg)
            mqtt.UserProperties.Add(new MqttUserProperty(MqttInboxHeaders.OutboxAggregateKey, agg));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublishTimeout);
        var result = await _client.PublishAsync(mqtt, timeout.Token).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MQTT publish failed for outbox message {message.Id}: {result.ReasonString ?? result.ReasonCode.ToString()}"); // pubsoft:exempt PUBSOFT027 "enum name render on non-hot path (doc-gen/startup/log line)"
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connected && _client.IsConnected) return;
        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connected && _client.IsConnected) return;
            var uri = new Uri(_options.BrokerUri);
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(_options.ClientId)
                .WithTcpServer(uri.Host, uri.Port)
                .WithCleanSession(!_options.PersistentSession);
            if (_options.Username is not null)
                builder.WithCredentials(_options.Username, _options.Password ?? "");
            if (string.Equals(uri.Scheme, "mqtts", System.StringComparison.Ordinal)) builder.WithTlsOptions(o => o.UseTls());
            await _client.ConnectAsync(builder.Build(), ct).ConfigureAwait(false);
            _connected = true;
        }
        finally { _connectGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync().ConfigureAwait(false);
        _client.Dispose();
        _connectGate.Dispose();
    }
}

/// <summary>MQTT5 user-property headers carried on published outbox messages.</summary>
public static class MqttInboxHeaders
{
    /// <summary>OutboxMessage.Id — consumer-side dedup key (InboxReceipt.MessageId).</summary>
    public const string OutboxMessageId = "x-outbox-id";
    /// <summary>OutboxMessage.AggregateKey — optional ordering/ownership hint.</summary>
    public const string OutboxAggregateKey = "x-outbox-aggkey";
}

public static class MqttOutboxServiceCollectionExtensions
{
    /// <summary>Registers the MQTT outbox dispatcher as the IOutboxDispatcher implementation.</summary>
    public static IServiceCollection AddPubSoftMqttOutboxDispatcher(
        this IServiceCollection services, Action<MqttOutboxOptions>? configure = null)
    {
        var options = new MqttOutboxOptions();
        configure?.Invoke(options);
        services.AddSingleton(options.CloneAndValidate());
        services.AddSingleton<MqttOutboxDispatcher>();
        services.AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<MqttOutboxDispatcher>());
        return services;
    }
}
