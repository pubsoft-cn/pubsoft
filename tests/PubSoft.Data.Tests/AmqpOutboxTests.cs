using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Amqp;
using PubSoft.Messaging.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AmqpOutboxTests
{
    private static OutboxMessage Msg(string eventType, string payloadJson)
    {
        var msg = (OutboxMessage)typeof(OutboxMessage)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)!
            .Invoke(null);
        foreach (var (n, v) in new (string, object?)[]
        {
            ("Id", PubSoft.Testing.TestIds.Next()), ("EventType", eventType), ("PayloadJson", payloadJson),
            ("OccurredAt", DateTimeOffset.UtcNow), ("Status", OutboxMessageStatus.Pending),
            ("AttemptCount", 0), ("MaxAttempts", 3),
        })
            typeof(OutboxMessage).GetProperty(n)!.SetValue(msg, v);
        return msg;
    }

    [Fact]
    public void OptionsDefaults()
    {
        var o = new AmqpOutboxOptions();
        Assert.Equal("amqp://guest:guest@localhost:5672/", o.BrokerUri);
        Assert.Equal("pubsoft.outbox", o.Exchange);
        Assert.Equal(ExchangeType.Topic, o.ExchangeType);
        Assert.Equal("outbox.", o.RoutingKeyPrefix);
        Assert.True(o.Mandatory);
        Assert.Equal(1_048_576, o.MaxPayloadBytes);
        Assert.Equal(TimeSpan.FromSeconds(10), o.PublishTimeout);
    }

    [Fact]
    public void CtorValidatesOptions()
    {
        Assert.Throws<ArgumentException>(() => new AmqpOutboxDispatcher(new AmqpOutboxOptions { BrokerUri = "" }));
        Assert.Throws<ArgumentException>(() => new AmqpOutboxDispatcher(new AmqpOutboxOptions { BrokerUri = "http://x" }));
        Assert.Throws<ArgumentException>(() => new AmqpOutboxDispatcher(new AmqpOutboxOptions { Exchange = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmqpOutboxDispatcher(new AmqpOutboxOptions { MaxPayloadBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmqpOutboxDispatcher(new AmqpOutboxOptions { PublishTimeout = TimeSpan.Zero }));
    }

    [Fact]
    public async Task ServiceRegistrationWiresDispatcher()
    {
        var s = new ServiceCollection();
        s.AddPubSoftAmqpOutboxDispatcher(o => o.BrokerUri = "amqp://guest:guest@b:5672/");
        await using var sp = s.BuildServiceProvider();
        Assert.IsType<AmqpOutboxDispatcher>(sp.GetRequiredService<IOutboxDispatcher>());
    }

    [Fact]
    public async Task DispatchPublishesRoutingKeyHeadersAndPersistentFlag()
    {
        var channel = new FakeChannel();
        await using var d = new AmqpOutboxDispatcher(
            new AmqpOutboxOptions { RoutingKeyPrefix = "ext." }, null, _ => Task.FromResult<IChannel>(channel));
        var msg = Msg("order.created", """{"id":7}""");
        typeof(OutboxMessage).GetProperty("AggregateKey")!.SetValue(msg, "order-7");
        typeof(OutboxMessage).GetProperty("CorrelationId")!.SetValue(msg, "trace-1");

        await d.DispatchAsync(msg);

        Assert.Equal("ext.order.created", channel.LastRoutingKey);
        Assert.Equal("pubsoft.outbox", channel.LastExchange);
        Assert.True(channel.LastMandatory);
        Assert.True(channel.LastPersistent);
        var headers = channel.LastHeaders!;
        Assert.Equal(msg.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), headers[OutboxHeaders.OutboxMessageId]);
        Assert.Equal("order-7", headers[OutboxHeaders.OutboxAggregateKey]);
        Assert.Equal("trace-1", headers[OutboxHeaders.OutboxCorrelationId]);
        Assert.Equal("trace-1", channel.LastCorrelationId);
        Assert.Equal("""{"id":7}""", System.Text.Encoding.UTF8.GetString(channel.LastBody.ToArray()));
        Assert.True(channel.ExchangeDeclared);
        Assert.True(channel.ExchangeDeclareDurable);
    }

    [Fact]
    public async Task UnroutableReturnFailsDispatchForRetry()
    {
        var channel = new FakeChannel { ReturnPublishedMessages = true };
        await using var d = new AmqpOutboxDispatcher(
            new AmqpOutboxOptions(), null, _ => Task.FromResult<IChannel>(channel));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => d.DispatchAsync(Msg("e", "{}")));
        Assert.Contains("unroutable", ex.Message);
    }

    [Fact]
    public async Task OversizePayloadRejectedBeforePublish()
    {
        var channel = new FakeChannel();
        await using var d = new AmqpOutboxDispatcher(
            new AmqpOutboxOptions { MaxPayloadBytes = 8 }, null, _ => Task.FromResult<IChannel>(channel));
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.DispatchAsync(Msg("e", new string('x', 64))));
        Assert.False(channel.Published);
    }

    private sealed class FakeChannel : IChannel
    {
        public bool Published;
        public bool ReturnPublishedMessages;
        public string? LastExchange;
        public string? LastRoutingKey;
        public bool LastMandatory;
        public bool LastPersistent;
        public string? LastCorrelationId;
        public IDictionary<string, object?>? LastHeaders;
        public ReadOnlyMemory<byte> LastBody;
        public bool ExchangeDeclared;
        public bool ExchangeDeclareDurable;
        private AsyncEventHandler<BasicReturnEventArgs>? _returnHandler;

        public int ChannelNumber => 1;
        public ShutdownEventArgs? CloseReason => null;
        public IAsyncBasicConsumer? DefaultConsumer { get; set; }
        public bool IsClosed => false;
        public bool IsOpen => true;
        public string? CurrentQueue => null;
        public TimeSpan ContinuationTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public void Dispose() { }

        public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync
        {
            add => _returnHandler += value;
            remove => _returnHandler -= value;
        }
        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync { add { } remove { } }
        public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync { add { } remove { } }
        public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync { add { } remove { } }

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete,
            IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false,
            CancellationToken cancellationToken = default)
        {
            ExchangeDeclared = true;
            ExchangeDeclareDurable = durable;
            return Task.CompletedTask;
        }

        public async ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            Published = true;
            LastExchange = exchange;
            LastRoutingKey = routingKey;
            LastMandatory = mandatory;
            LastHeaders = basicProperties.Headers;
            LastBody = body;
            LastCorrelationId = basicProperties.CorrelationId;
            LastPersistent = basicProperties.Persistent;
            if (ReturnPublishedMessages && _returnHandler is { } handler)
            {
                var args = new BasicReturnEventArgs(
                    312, "NO_ROUTE", exchange, routingKey, basicProperties, body);
                foreach (var d in handler.GetInvocationList())
                    await ((AsyncEventHandler<BasicReturnEventArgs>)d)(this, args);
            }
        }

        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
            => throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task BasicCancelAsync(string consumerTag, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object?>? arguments, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(ushort replyCode, string replyText, bool abort, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(ShutdownEventArgs reason, bool abort) => throw new NotImplementedException();
        public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeBindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeUnbindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> QueueDeleteAsync(string queue, bool ifUnused, bool ifEmpty, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> QueuePurgeAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QueueBindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> MessageCountAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> ConsumerCountAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxCommitAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxRollbackAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxSelectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}

public sealed class AmqpOutboxIntegrationTests
{
    private sealed class AmqpFactAttribute : FactAttribute
    {
        public AmqpFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_AMQP")))
                Skip = "Set PUBSOFT_AMQP to run AMQP broker integration tests.";
        }
    }

    private static OutboxMessage Msg(string eventType, string payloadJson)
    {
        var msg = (OutboxMessage)typeof(OutboxMessage)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)!
            .Invoke(null);
        foreach (var (n, v) in new (string, object?)[]
        {
            ("Id", PubSoft.Testing.TestIds.Next()), ("EventType", eventType), ("PayloadJson", payloadJson),
            ("OccurredAt", DateTimeOffset.UtcNow), ("Status", OutboxMessageStatus.Pending),
            ("AttemptCount", 0), ("MaxAttempts", 3),
        })
            typeof(OutboxMessage).GetProperty(n)!.SetValue(msg, v);
        return msg;
    }

    [AmqpFact]
    public async Task RoundtripAgainstRealBroker()
    {
        var broker = Environment.GetEnvironmentVariable("PUBSOFT_AMQP")!;
        var exchange = "pubsoft.test.outbox";
        var queue = "pubsoft.test.q";
        var routingKey = "outbox.roundtrip.test";

        var factory = new ConnectionFactory { Uri = new Uri(broker) };
        await using var conn = await factory.CreateConnectionAsync();
        await using var setup = await conn.CreateChannelAsync();
        // exchange args must match the dispatcher's own declare (durable topic, no auto-delete).
        await setup.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        // RabbitMQ 4 rejects transient non-exclusive queues -- declare durable and clean up explicitly.
        await setup.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await setup.QueueBindAsync(queue, exchange, routingKey);
        try
        {
            await using var d = new AmqpOutboxDispatcher(new AmqpOutboxOptions
            {
                BrokerUri = broker,
                Exchange = exchange,
                RoutingKeyPrefix = "outbox.",
            });
            var msg = Msg("roundtrip.test", """{"ok":true}""");
            await d.DispatchAsync(msg); // throws unless the broker confirms

            var result = await setup.BasicGetAsync(queue, autoAck: true);
            Assert.NotNull(result);
            Assert.Equal("""{"ok":true}""", System.Text.Encoding.UTF8.GetString(result!.Body.ToArray()));
            // AMQP longstr header values surface as byte[] on the client.
            Assert.Equal(msg.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Text.Encoding.UTF8.GetString((byte[])result.BasicProperties.Headers![OutboxHeaders.OutboxMessageId]!));
        }
        finally
        {
            await setup.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            await setup.ExchangeDeleteAsync(exchange);
        }
    }

    [AmqpFact]
    public async Task UnroutableAgainstRealBrokerFailsDispatch()
    {
        var broker = Environment.GetEnvironmentVariable("PUBSOFT_AMQP")!;
        await using var d = new AmqpOutboxDispatcher(new AmqpOutboxOptions
        {
            BrokerUri = broker,
            Exchange = "pubsoft.test.unbound",
            RoutingKeyPrefix = "no.bound.queue.",
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.DispatchAsync(Msg("e", "{}")));
    }
}
