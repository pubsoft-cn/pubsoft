using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Amqp;
using PubSoft.Messaging.Outbox;

// Options defaults align with the broker contract: durable topic exchange,
// mandatory flag, 1 MiB cap, 10s confirm timeout.
var options = new AmqpOutboxOptions();
if (options.BrokerUri != "amqp://guest:guest@localhost:5672/"
    || options.Exchange != "pubsoft.outbox"
    || options.ExchangeType != RabbitMQ.Client.ExchangeType.Topic
    || options.RoutingKeyPrefix != "outbox."
    || !options.Mandatory
    || options.MaxPayloadBytes != 1_048_576
    || options.PublishTimeout != TimeSpan.FromSeconds(10))
    throw new InvalidOperationException("AmqpOutboxOptions defaults changed.");

// Option guards fire through the dispatcher ctor (CloneAndValidate is internal).
await using var _ = new AmqpOutboxDispatcher(new AmqpOutboxOptions());
try { await using var d = new AmqpOutboxDispatcher(new AmqpOutboxOptions { BrokerUri = " " }); throw new InvalidOperationException("bad uri accepted"); }
catch (ArgumentException) { }
try { await using var d = new AmqpOutboxDispatcher(new AmqpOutboxOptions { BrokerUri = "http://x" }); throw new InvalidOperationException("bad scheme accepted"); }
catch (ArgumentException) { }
try { await using var d = new AmqpOutboxDispatcher(new AmqpOutboxOptions { MaxPayloadBytes = 0 }); throw new InvalidOperationException("bad cap accepted"); }
catch (ArgumentOutOfRangeException) { }

// DI wires the dispatcher as the outbox transport.
var services = new ServiceCollection();
services.AddPubSoftAmqpOutboxDispatcher(o => o.BrokerUri = "amqp://guest:guest@example:5672/");
await using var provider = services.BuildServiceProvider();
if (provider.GetRequiredService<IOutboxDispatcher>() is not AmqpOutboxDispatcher)
    throw new InvalidOperationException("IOutboxDispatcher not wired to AmqpOutboxDispatcher.");

// Outbox identity travels as transport-agnostic AMQP headers.
if (OutboxHeaders.OutboxMessageId != "x-outbox-id" || OutboxHeaders.OutboxAggregateKey != "x-outbox-aggkey")
    throw new InvalidOperationException("Outbox header contract changed.");

Console.WriteLine("PubSoft.Messaging.Amqp consumer smoke passed.");
