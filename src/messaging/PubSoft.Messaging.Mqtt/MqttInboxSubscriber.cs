using System.Text;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Mqtt;

/// <summary>Options for the MQTT inbox subscriber (consumer side of the outbox transport).</summary>
public sealed class MqttInboxOptions
{
    /// <summary>Broker URI (mqtt://host:port or mqtts://host:port).</summary>
    public string BrokerUri { get; set; } = "mqtt://localhost:1883";
    /// <summary>MQTT client identifier — must be unique per instance.</summary>
    public string ClientId { get; set; } = "pubsoft-inbox";
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Topic filter, e.g. "ext/outbox/#".</summary>
    public string TopicFilter { get; set; } = "outbox/#";
    /// <summary>
    /// Shared-subscription group name. When set the filter becomes
    /// $share/{ShareGroup}/{TopicFilter} — instances in the same group
    /// load-balance deliveries instead of each receiving every message.
    /// </summary>
    public string? ShareGroup { get; set; } = "outbox-workers";
    /// <summary>Consumer identity for InboxReceipt dedup (per logical consumer, not per instance).</summary>
    public string ConsumerName { get; set; } = "default";
    /// <summary>Subscription QoS — AtLeastOnce pairs with inbox dedup.</summary>
    public int QualityOfService { get; set; } = 1;
    /// <summary>Persistent session (clean_start=0) so the broker queues messages while offline.</summary>
    public bool PersistentSession { get; set; } = true;
    /// <summary>
    /// MQTT5 Session Expiry Interval in seconds. Required for offline-queue durability:
    /// without it the broker destroys the session on disconnect even with clean_start=0.
    /// Default 86400 (24h) — applied only when PersistentSession is true; set 0 for
    /// "expire at network disconnect" semantics.
    /// </summary>
    public uint SessionExpirySeconds { get; set; } = 86_400;
    /// <summary>Reconnect delay after a broker disconnect.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Prefix stripped from the topic to derive the event type; empty → full topic used.</summary>
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

/// <summary>
/// Background subscriber that consumes MQTT-published outbox messages and feeds
/// them through <see cref="IInboxMessageProcessor"/> — dedup via InboxReceipt
/// keyed on the message's x-outbox-id user property, then invokes the handler.
/// </summary>
public sealed class MqttInboxSubscriber : BackgroundService
{
    private readonly MqttInboxOptions _options;
    private readonly IInboxMessageProcessor _inbox;
    private readonly Func<string, string, CancellationToken, Task> _handler;

    /// <param name="options">Inbox subscriber configuration (broker, topic filter, session).</param>
    /// <param name="inbox">Inbox dedup pipeline — receipts are recorded through IInboxMessageProcessor.</param>
    /// <param name="handler">(eventType, payloadJson, ct) — invoked once per unique message id.</param>
    public MqttInboxSubscriber(
        MqttInboxOptions options,
        IInboxMessageProcessor inbox,
        Func<string, string, CancellationToken, Task> handler)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).CloneAndValidate();
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e => HandleAsync(e, stoppingToken);

        var filter = _options.ShareGroup is { Length: > 0 } group
            ? $"$share/{group}/{_options.TopicFilter}"
            : _options.TopicFilter;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                    await ConnectAsync(client, stoppingToken).ConfigureAwait(false);
                await client.SubscribeAsync(filter, (MqttQualityOfServiceLevel)_options.QualityOfService, stoppingToken).ConfigureAwait(false);
                // idle until disconnected or stopped
                while (client.IsConnected && !stoppingToken.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private Task ConnectAsync(IMqttClient client, CancellationToken ct)
    {
        var uri = new Uri(_options.BrokerUri);
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(uri.Host, uri.Port)
            .WithCleanSession(!_options.PersistentSession);
        if (_options.PersistentSession)
            builder.WithSessionExpiryInterval(_options.SessionExpirySeconds);
        if (_options.Username is not null)
            builder.WithCredentials(_options.Username, _options.Password ?? "");
        if (uri.Scheme == "mqtts") builder.WithTlsOptions(o => o.UseTls());
        return client.ConnectAsync(builder.Build(), ct);
    }

    private async Task HandleAsync(MqttApplicationMessageReceivedEventArgs e, CancellationToken ct)
    {
        var messageId = ExtractMessageId(e.ApplicationMessage);
        if (messageId is null)
            return; // not an outbox message — ack and drop
        var eventType = e.ApplicationMessage.Topic.StartsWith(_options.EventTypePrefix, StringComparison.Ordinal)
            ? e.ApplicationMessage.Topic[_options.EventTypePrefix.Length..]
            : e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.Payload.IsEmpty ? "" : Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
        await _inbox.ProcessOnceAsync(
            _options.ConsumerName,
            messageId.Value,
            token => _handler(eventType, payload, token),
            ct).ConfigureAwait(false);
    }

    internal static Guid? ExtractMessageId(MqttApplicationMessage message)
    {
        var prop = message.UserProperties?.FirstOrDefault(p => p.Name == MqttInboxHeaders.OutboxMessageId);
        return prop is not null && Guid.TryParseExact(prop.Value, "N", out var id) ? id : null;
    }
}
