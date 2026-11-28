using System.Text;
using PubSoft.Messaging.Amqp;
using PubSoft.Messaging.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AmqpInboxTests
{
    [Fact]
    public void InboxOptionsDefaults()
    {
        var o = new AmqpInboxOptions();
        Assert.Equal("amqp://guest:guest@localhost:5672/", o.BrokerUri);
        Assert.Equal("pubsoft.outbox.inbox", o.Queue);
        Assert.Equal("pubsoft.outbox", o.Exchange);
        Assert.Equal(ExchangeType.Topic, o.ExchangeType);
        Assert.Equal("outbox.#", o.BindingKey);
        Assert.True(o.DeclareTopology);
        Assert.Equal("default", o.ConsumerName);
        Assert.Equal((ushort)16, o.PrefetchCount);
        Assert.Equal("outbox.", o.EventTypePrefix);
    }

    [Fact]
    public void InboxCtorValidates()
    {
        Assert.Throws<ArgumentException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions { BrokerUri = "http://x" }, new FakeInbox(), (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions { Queue = " " }, new FakeInbox(), (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions { ConsumerName = " " }, new FakeInbox(), (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions { PrefetchCount = 0 }, new FakeInbox(), (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions(), null!, (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions { ReconnectJitterRatio = double.NaN }, new FakeInbox(), (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmqpInboxSubscriber(
            new AmqpInboxOptions { ReconnectJitterRatio = -0.5 }, new FakeInbox(), (_, _, _) => Task.CompletedTask));
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("0", null)]
    [InlineData("-3", null)]
    [InlineData("not-a-number", null)]
    public void ExtractMessageIdReadsLongstrHeader(string? value, long? expected)
    {
        var properties = new BasicProperties
        {
            Headers = value is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [OutboxHeaders.OutboxMessageId] = Encoding.UTF8.GetBytes(value)
                }
        };

        Assert.Equal(expected, AmqpInboxSubscriber.ExtractMessageId(properties));
    }

    [Fact]
    public void ExtractMessageIdReturnsNullWithoutHeaders()
    {
        Assert.Null(AmqpInboxSubscriber.ExtractMessageId(new BasicProperties()));
    }

    [Fact]
    public async Task DeliveryRunsDedupHandlerAndAcks()
    {
        var channel = new FakeChannel();
        var inbox = new FakeInbox();
        var received = new List<(string EventType, string Payload)>();
        using var subscriber = CreateSubscriber(channel, inbox,
            (eventType, payload, _) =>
            {
                lock (received) received.Add((eventType, payload));
                return Task.CompletedTask;
            });

        await subscriber.StartAsync(default);
        await channel.WaitForConsumeAsync();
        await channel.DeliverAsync(
            deliveryTag: 7,
            routingKey: "outbox.order.created",
            outboxId: 42,
            payload: """{"id":7}""");
        await channel.WaitForAckAsync();
        await subscriber.StopAsync(default);

        var call = Assert.Single(inbox.Calls);
        Assert.Equal("order-svc", call.Consumer);
        Assert.Equal(42, call.Id);
        var delivery = Assert.Single(received);
        Assert.Equal("order.created", delivery.EventType);
        Assert.Equal("""{"id":7}""", delivery.Payload);
        Assert.Equal(7UL, channel.Acks.Single());
        Assert.Empty(channel.Nacks);
        Assert.True(channel.TopologyDeclared);
        Assert.Equal((ushort)16, channel.LastPrefetchCount);
    }

    [Fact]
    public async Task DuplicateDeliveryAcksWithoutReinvokingHandler()
    {
        var channel = new FakeChannel();
        var inbox = new FakeInbox { Result = false };
        var handlerCalls = 0;
        using var subscriber = CreateSubscriber(channel, inbox,
            (_, _, _) => { Interlocked.Increment(ref handlerCalls); return Task.CompletedTask; });

        await subscriber.StartAsync(default);
        await channel.WaitForConsumeAsync();
        await channel.DeliverAsync(9, "outbox.e", 100, "{}");
        await channel.WaitForAckAsync();
        await subscriber.StopAsync(default);

        Assert.Equal(0, handlerCalls);
        Assert.Equal(9UL, channel.Acks.Single());
    }

    [Fact]
    public async Task MessageWithoutOutboxIdIsAckedAndDropped()
    {
        var channel = new FakeChannel();
        var inbox = new FakeInbox();
        using var subscriber = CreateSubscriber(channel, inbox, (_, _, _) => Task.CompletedTask);

        await subscriber.StartAsync(default);
        await channel.WaitForConsumeAsync();
        await channel.DeliverAsync(3, "outbox.e", outboxId: null, payload: "{}");
        await channel.WaitForAckAsync();
        await subscriber.StopAsync(default);

        Assert.Empty(inbox.Calls);
        Assert.Equal(3UL, channel.Acks.Single());
    }

    [Fact]
    public async Task HandlerFailureNacksWithRequeue()
    {
        var channel = new FakeChannel();
        var inbox = new FakeInbox();
        using var subscriber = CreateSubscriber(channel, inbox,
            (_, _, _) => throw new InvalidOperationException("handler blew up"));

        await subscriber.StartAsync(default);
        await channel.WaitForConsumeAsync();
        await channel.DeliverAsync(5, "outbox.e", 55, "{}");
        await channel.WaitForNackAsync();
        await subscriber.StopAsync(default);

        var nack = Assert.Single(channel.Nacks);
        Assert.Equal(5UL, nack.DeliveryTag);
        Assert.True(nack.Requeue);
        Assert.Empty(channel.Acks);
    }

    private static AmqpInboxSubscriber CreateSubscriber(
        FakeChannel channel,
        IInboxMessageProcessor inbox,
        Func<string, string, CancellationToken, Task> handler)
        => new(
            new AmqpInboxOptions
            {
                ConsumerName = "order-svc",
                ReconnectDelay = TimeSpan.FromMilliseconds(10)
            },
            inbox,
            handler,
            logger: null,
            channelFactory: _ => Task.FromResult<IChannel>(channel));

    private sealed class FakeInbox : IInboxMessageProcessor
    {
        public bool Result { get; set; } = true;
        public List<(string Consumer, long Id)> Calls { get; } = [];

        public async Task<bool> ProcessOnceAsync(
            string consumerName,
            long messageId,
            Func<CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((consumerName, messageId));
            if (!Result) return false;
            await handler(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private sealed class FakeChannel : IChannel
    {
        private readonly TaskCompletionSource _consumeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _acked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _nacked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IAsyncBasicConsumer? _consumer;

        public List<ulong> Acks { get; } = [];
        public List<(ulong DeliveryTag, bool Requeue)> Nacks { get; } = [];
        public bool TopologyDeclared { get; private set; }
        public ushort LastPrefetchCount { get; private set; }
        public string? ConsumedQueue { get; private set; }

        public Task WaitForConsumeAsync() => _consumeStarted.Task;
        public Task WaitForAckAsync() => _acked.Task;
        public Task WaitForNackAsync() => _nacked.Task;

        public Task DeliverAsync(ulong deliveryTag, string routingKey, long? outboxId, string payload)
        {
            var properties = new BasicProperties();
            if (outboxId is { } id)
            {
                properties.Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [OutboxHeaders.OutboxMessageId] = Encoding.UTF8.GetBytes(
                        id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                };
            }
            return _consumer!.HandleBasicDeliverAsync(
                "test-consumer", deliveryTag, redelivered: false,
                "pubsoft.outbox", routingKey, properties,
                Encoding.UTF8.GetBytes(payload), CancellationToken.None);
        }

        public int ChannelNumber => 1;
        public ShutdownEventArgs? CloseReason => null;
        public IAsyncBasicConsumer? DefaultConsumer { get; set; }
        public bool IsClosed => false;
        public bool IsOpen => true;
        public string? CurrentQueue => null;
        public TimeSpan ContinuationTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync { add { } remove { } }
        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync { add { } remove { } }
        public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync { add { } remove { } }
        public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync { add { } remove { } }

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete,
            IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete,
            IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new QueueDeclareOk(queue, 0, 0));

        public Task QueueBindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false,
            CancellationToken cancellationToken = default)
        {
            TopologyDeclared = true;
            return Task.CompletedTask;
        }

        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global,
            CancellationToken cancellationToken = default)
        {
            LastPrefetchCount = prefetchCount;
            return Task.CompletedTask;
        }

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag,
            bool noLocal, bool exclusive, IDictionary<string, object?>? arguments,
            IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
        {
            _consumer = consumer;
            ConsumedQueue = queue;
            _consumeStarted.TrySetResult();
            return Task.FromResult("test-consumer");
        }

        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple,
            CancellationToken cancellationToken = default)
        {
            lock (Acks) Acks.Add(deliveryTag);
            _acked.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue,
            CancellationToken cancellationToken = default)
        {
            lock (Nacks) Nacks.Add((deliveryTag, requeue));
            _nacked.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public Task BasicCancelAsync(string consumerTag, bool noWait = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, IDisposable? publishActivity, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, IDisposable? publishActivity, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory,
            TProperties basicProperties, System.Buffers.ReadOnlySequence<byte> body, IDisposable? publishActivity, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory,
            TProperties basicProperties, System.Buffers.ReadOnlySequence<byte> body, IDisposable? publishActivity, CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(ushort replyCode, string replyText, bool abort, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(ShutdownEventArgs reason, bool abort) => throw new NotImplementedException();
        public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeBindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeUnbindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> QueueDeleteAsync(string queue, bool ifUnused, bool ifEmpty, bool noWait = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> QueuePurgeAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> MessageCountAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> ConsumerCountAsync(string queue, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxCommitAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxRollbackAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxSelectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}

public sealed class AmqpInboxIntegrationTests
{
    private sealed class AmqpFactAttribute : FactAttribute
    {
        public AmqpFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_AMQP")))
                Skip = "Set PUBSOFT_AMQP to run AMQP broker integration tests.";
        }
    }

    [AmqpFact]
    public async Task InboxRoundtripAgainstRealBroker()
    {
        var broker = Environment.GetEnvironmentVariable("PUBSOFT_AMQP")!;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var exchange = $"pubsoft.test.inbox.{suffix}";
        var queue = $"pubsoft.test.inbox.{suffix}";

        var delivered = new TaskCompletionSource<(string EventType, string Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inbox = new RecordingInbox();
        var subscriber = new AmqpInboxSubscriber(
            new AmqpInboxOptions
            {
                BrokerUri = broker,
                Exchange = exchange,
                Queue = queue,
                ConsumerName = "it-consumer",
                ReconnectDelay = TimeSpan.FromMilliseconds(200)
            },
            inbox,
            (eventType, payload, _) =>
            {
                delivered.TrySetResult((eventType, payload));
                return Task.CompletedTask;
            });

        await using var cleanup = await new ConnectionFactory { Uri = new Uri(broker) }.CreateConnectionAsync();
        await using var cleanupChannel = await cleanup.CreateChannelAsync();
        try
        {
            await subscriber.StartAsync(default);
            // subscriber declares exchange/queue/binding itself; give it a beat to subscribe
            await Task.Delay(1500);

            var msg = (OutboxMessage)typeof(OutboxMessage)
                .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)!
                .Invoke(null);
            foreach (var (n, v) in new (string, object?)[]
            {
                ("Id", PubSoft.Testing.TestIds.Next()), ("EventType", "roundtrip.test"),
                ("PayloadJson", """{"ok":true}"""), ("OccurredAt", DateTimeOffset.UtcNow),
                ("Status", OutboxMessageStatus.Pending), ("AttemptCount", 0), ("MaxAttempts", 3),
            })
                typeof(OutboxMessage).GetProperty(n)!.SetValue(msg, v);

            await using var dispatcher = new AmqpOutboxDispatcher(new AmqpOutboxOptions
            {
                BrokerUri = broker,
                Exchange = exchange,
            });
            await dispatcher.DispatchAsync(msg);

            var result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("roundtrip.test", result.EventType);
            Assert.Equal("""{"ok":true}""", result.Payload);
            Assert.Equal(msg.Id, inbox.Calls.Single().Id);
        }
        finally
        {
            await subscriber.StopAsync(default);
            subscriber.Dispose();
            await cleanupChannel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            await cleanupChannel.ExchangeDeleteAsync(exchange);
        }
    }

    private sealed class RecordingInbox : IInboxMessageProcessor
    {
        public List<(string Consumer, long Id)> Calls { get; } = [];

        public async Task<bool> ProcessOnceAsync(
            string consumerName, long messageId,
            Func<CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        {
            Calls.Add((consumerName, messageId));
            await handler(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
