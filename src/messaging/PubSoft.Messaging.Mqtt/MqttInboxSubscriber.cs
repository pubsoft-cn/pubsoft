using System.Text;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Microsoft.Extensions.Hosting;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Mqtt;


/// <summary>
/// Background subscriber that consumes MQTT-published outbox messages and feeds
/// them through <see cref="IInboxMessageProcessor"/> -- dedup via InboxReceipt
/// keyed on the message's x-outbox-id user property, then invokes the handler.
/// </summary>
public sealed class MqttInboxSubscriber : BackgroundService
{
    private readonly MqttInboxOptions _options;
    private readonly IInboxMessageProcessor _inbox;
    private readonly Func<string, string, CancellationToken, Task> _handler;

    /// <param name="options">Inbox subscriber configuration (broker, topic filter, session).</param>
    /// <param name="inbox">Inbox dedup pipeline -- receipts are recorded through IInboxMessageProcessor.</param>
    /// <param name="handler">(eventType, payloadJson, ct) -- invoked once per unique message id.</param>
    public MqttInboxSubscriber(
        MqttInboxOptions options,
        IInboxMessageProcessor inbox,
        Func<string, string, CancellationToken, Task> handler)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).CloneAndValidate();
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    internal static long? ExtractMessageId(MqttApplicationMessage message)
    {
        var prop = message.UserProperties?.FirstOrDefault(p => string.Equals(p.Name, MqttInboxHeaders.OutboxMessageId, System.StringComparison.Ordinal));
        return prop is not null && long.TryParse(prop.Value, out var id) && id > 0 ? id : null;
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
        if (string.Equals(uri.Scheme, "mqtts", System.StringComparison.Ordinal)) builder.WithTlsOptions(o => o.UseTls());
        return client.ConnectAsync(builder.Build(), ct);
    }

    private async Task HandleAsync(MqttApplicationMessageReceivedEventArgs e, CancellationToken ct)
    {
        var messageId = ExtractMessageId(e.ApplicationMessage);
        if (messageId is null)
            return; // not an outbox message - ack and drop
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
}
