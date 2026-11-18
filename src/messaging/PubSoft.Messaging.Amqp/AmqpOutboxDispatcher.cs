using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PubSoft.Messaging.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PubSoft.Messaging.Amqp;


/// <summary>
/// Outbox dispatcher that publishes committed outbox messages to an AMQP broker (RabbitMQ).
/// Delivery semantics: publisher confirms ack broker acceptance before the outbox marks the
/// message delivered; dedup on the consumer side stays the InboxReceipt contract.
/// </summary>
/// <remarks>
/// Messages publish with <see cref="AmqpOutboxOptions.Mandatory"/> so an unroutable routing
/// key is returned by the broker instead of silently dropped. The broker sends the return
/// before the confirm, so a returned message id recorded before confirm completion fails the
/// dispatch for retry. A return that races past that window is logged -- the outbox row was
/// already confirmed; topology (queue bindings) is a host responsibility.
/// </remarks>
public sealed class AmqpOutboxDispatcher : IOutboxDispatcher, IAsyncDisposable
{
    private readonly AmqpOutboxOptions _options;
    private readonly ConnectionFactory? _factory;
    private readonly Func<CancellationToken, Task<IChannel>>? _channelFactory;
    private readonly ILogger<AmqpOutboxDispatcher>? _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _unroutable = new(StringComparer.Ordinal);
    private IConnection? _connection;
    private IChannel? _channel;
    private int _disposed;

    /// <summary>Creates the dispatcher.</summary>
    public AmqpOutboxDispatcher(AmqpOutboxOptions options, ILogger<AmqpOutboxDispatcher>? logger = null)
        : this(options, logger, null)
    {
    }

    internal AmqpOutboxDispatcher(
        AmqpOutboxOptions options,
        ILogger<AmqpOutboxDispatcher>? logger,
        Func<CancellationToken, Task<IChannel>>? channelFactory)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).CloneAndValidate();
        _logger = logger;
        _channelFactory = channelFactory;
        if (channelFactory is null)
        {
            _factory = new ConnectionFactory { Uri = new Uri(_options.BrokerUri) };
        }
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var payload = Encoding.UTF8.GetBytes(message.PayloadJson);
        if (payload.Length > _options.MaxPayloadBytes)
            throw new InvalidOperationException(
                $"OutboxMessage {message.Id} payload ({payload.Length}B) exceeds broker limit ({_options.MaxPayloadBytes}B); use claim-check for large events.");

        var channel = await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);

        var messageId = message.Id.ToString(CultureInfo.InvariantCulture);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId,
            CorrelationId = message.CorrelationId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [OutboxHeaders.OutboxMessageId] = messageId,
            },
        };
        if (message.AggregateKey is { } aggregateKey)
            properties.Headers[OutboxHeaders.OutboxAggregateKey] = aggregateKey;
        if (message.CorrelationId is { } correlationId)
            properties.Headers[OutboxHeaders.OutboxCorrelationId] = correlationId;

        var routingKey = _options.RoutingKeyPrefix + message.EventType;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublishTimeout);
        // one channel, serialized sends -- the broker returns before it confirms, so the
        // post-confirm id check maps the return to this publish unambiguously.
        await _sendGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            _unroutable.TryRemove(messageId, out _);
            try
            {
                await channel.BasicPublishAsync(
                        _options.Exchange, routingKey, _options.Mandatory, properties, payload, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (RabbitMQ.Client.Exceptions.PublishReturnException ex)
            {
                // confirm tracking turns the broker's basic.return into this exception; surface a
                // transport-agnostic failure so the outbox retries instead of marking delivered.
                throw new InvalidOperationException(
                    $"AMQP broker returned outbox message {message.Id} unroutable (exchange '{_options.Exchange}', routing key '{routingKey}'): {ex.Message}",
                    ex);
            }
            if (_unroutable.TryRemove(messageId, out _))
                throw new InvalidOperationException(
                    $"AMQP broker returned outbox message {message.Id} unroutable (exchange '{_options.Exchange}', routing key '{routingKey}'); check queue bindings.");
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_channel is { } channel)
            await channel.DisposeAsync().ConfigureAwait(false);
        if (_connection is { } connection)
            await connection.DisposeAsync().ConfigureAwait(false);
        _sendGate.Dispose();
        _connectGate.Dispose();
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
            return open;

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true } current)
                return current;

            if (_channelFactory is { } factory)
            {
                _channel = await factory(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _connection = await _factory!.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                _channel = await _connection.CreateChannelAsync(
                        new CreateChannelOptions(
                            publisherConfirmationsEnabled: true,
                            publisherConfirmationTrackingEnabled: true),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _channel.BasicReturnAsync += OnBasicReturnAsync;
            await _channel.ExchangeDeclareAsync(
                    _options.Exchange, _options.ExchangeType, durable: true, autoDelete: false,
                    arguments: null, passive: false, noWait: false, cancellationToken)
                .ConfigureAwait(false);
            return _channel;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private Task OnBasicReturnAsync(object sender, BasicReturnEventArgs args)
    {
        var messageId = args.BasicProperties.MessageId;
        if (!string.IsNullOrEmpty(messageId))
            _unroutable[messageId] = 1;
        _logger?.LogWarning(
            "AMQP broker returned message {MessageId}: {ReplyCode} {ReplyText} (exchange '{Exchange}', routing key '{RoutingKey}')",
            messageId, args.ReplyCode, args.ReplyText, args.Exchange, args.RoutingKey);
        return Task.CompletedTask;
    }
}
