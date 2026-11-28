using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.Core;
using PubSoft.Messaging.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PubSoft.Messaging.Amqp;


/// <summary>
/// Background subscriber that consumes AMQP-published outbox messages and feeds
/// them through <see cref="IInboxMessageProcessor"/> -- dedup via InboxReceipt
/// keyed on the message's x-outbox-id header, then invokes the handler.
/// </summary>
/// <remarks>
/// Manual ack: a delivery is acked only after <see cref="IInboxMessageProcessor"/>
/// returns (processed or already-receipted). Processing failures nack with requeue
/// so the broker redelivers -- bound poison-message redelivery loops with a
/// dead-letter exchange or quorum-queue delivery limit on the broker side.
/// </remarks>
public sealed class AmqpInboxSubscriber : BackgroundService
{
    private readonly AmqpInboxOptions _options;
    private readonly IInboxMessageProcessor _inbox;
    private readonly Func<string, string, CancellationToken, Task> _handler;
    private readonly Func<CancellationToken, Task<IChannel>>? _channelFactory;
    private readonly ILogger<AmqpInboxSubscriber>? _logger;

    /// <param name="options">Inbox subscriber configuration (broker, queue, binding).</param>
    /// <param name="inbox">Inbox dedup pipeline -- receipts are recorded through IInboxMessageProcessor.</param>
    /// <param name="handler">(eventType, payloadJson, ct) -- invoked once per unique message id.</param>
    /// <param name="logger">Optional logger for reconnect and failure diagnostics.</param>
    public AmqpInboxSubscriber(
        AmqpInboxOptions options,
        IInboxMessageProcessor inbox,
        Func<string, string, CancellationToken, Task> handler,
        ILogger<AmqpInboxSubscriber>? logger = null)
        : this(options, inbox, handler, logger, null)
    {
    }

    internal AmqpInboxSubscriber(
        AmqpInboxOptions options,
        IInboxMessageProcessor inbox,
        Func<string, string, CancellationToken, Task> handler,
        ILogger<AmqpInboxSubscriber>? logger,
        Func<CancellationToken, Task<IChannel>>? channelFactory)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).CloneAndValidate();
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger;
        _channelFactory = channelFactory;
    }

    /// <summary>The dedup key: x-outbox-id header (longstr surfaces as byte[]).</summary>
    internal static long? ExtractMessageId(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null
            || !properties.Headers.TryGetValue(OutboxHeaders.OutboxMessageId, out var raw)
            || raw is null)
            return null;

        var text = raw switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => raw.ToString()
        };
        return long.TryParse(text, out var id) && id > 0 ? id : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeLoopAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) // pubsoft:exempt PUBSOFT045 "broker lost — backoff then the loop reconnects"
            {
                var reconnectDelay = BoundedJitter.Apply(_options.ReconnectDelay, _options.ReconnectJitterRatio);
                _logger?.LogWarning(exception,
                    "AMQP inbox connection lost; reconnecting in {Delay}.", reconnectDelay);
                await Task.Delay(reconnectDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        IConnection? connection = null;
        IChannel channel;
        if (_channelFactory is { } factory)
        {
            channel = await factory(stoppingToken).ConfigureAwait(false);
        }
        else
        {
            connection = await new ConnectionFactory { Uri = new Uri(_options.BrokerUri) }
                .CreateConnectionAsync(stoppingToken).ConfigureAwait(false);
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken)
                .ConfigureAwait(false);
        }

        await using (connection)
        await using (channel)
        {
            if (_options.DeclareTopology)
            {
                await channel.ExchangeDeclareAsync(
                        _options.Exchange, _options.ExchangeType, durable: true, autoDelete: false,
                        arguments: null, passive: false, noWait: false, stoppingToken)
                    .ConfigureAwait(false);
                await channel.QueueDeclareAsync(
                        _options.Queue, durable: true, exclusive: false, autoDelete: false,
                        arguments: null, passive: false, noWait: false, stoppingToken)
                    .ConfigureAwait(false);
                await channel.QueueBindAsync(
                        _options.Queue, _options.Exchange, _options.BindingKey,
                        arguments: null, noWait: false, stoppingToken)
                    .ConfigureAwait(false);
            }

            await channel.BasicQosAsync(
                    prefetchSize: 0, _options.PrefetchCount, global: false, stoppingToken)
                .ConfigureAwait(false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => OnMessageAsync(channel, args, stoppingToken);
            var consumerTag = await channel.BasicConsumeAsync(
                    _options.Queue, autoAck: false, consumer, stoppingToken)
                .ConfigureAwait(false);
            _logger?.LogInformation("AMQP inbox consuming queue '{Queue}' as '{Tag}'.", _options.Queue, consumerTag);

            // idle until the channel drops or the host stops; a closed channel forces reconnect
            while (channel.IsOpen && !stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            if (channel.IsOpen)
            {
                try
                {
                    await channel.BasicCancelAsync(consumerTag, noWait: false, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger?.LogDebug(exception, "AMQP inbox consumer cancel failed during shutdown.");
                }
            }
            else
            {
                throw new System.IO.IOException("AMQP inbox channel closed by broker.");
            }
        }
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        try
        {
            var messageId = ExtractMessageId(args.BasicProperties);
            if (messageId is null)
            {
                // not an outbox message - ack and drop
                _logger?.LogDebug("AMQP inbox delivery {DeliveryTag} has no {Header}; dropping.",
                    args.DeliveryTag, OutboxHeaders.OutboxMessageId);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken).ConfigureAwait(false);
                return;
            }

            var eventType = args.RoutingKey.StartsWith(_options.EventTypePrefix, StringComparison.Ordinal)
                ? args.RoutingKey[_options.EventTypePrefix.Length..]
                : args.RoutingKey;
            var payload = args.Body.IsEmpty ? "" : Encoding.UTF8.GetString(args.Body.Span);
            await _inbox.ProcessOnceAsync(
                _options.ConsumerName,
                messageId.Value,
                token => _handler(eventType, payload, token),
                stoppingToken).ConfigureAwait(false);
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception,
                "AMQP inbox delivery {DeliveryTag} processing failed; nacking for redelivery.", args.DeliveryTag);
            try
            {
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception nackException)
            {
                _logger?.LogError(nackException,
                    "AMQP inbox nack failed for delivery {DeliveryTag}; the broker redelivers on reconnect.", args.DeliveryTag);
            }
        }
    }
}
