using System.Text.Json;
using PubSoft.Messaging;

var envelope = MessageEnvelope.Create(
    "event-42",
    "orders.created.v1",
    new OrderCreated(42),
    DateTimeOffset.UtcNow,
    source: "orders");
var publisher = new ConsumerPublisher();
await publisher.PublishAsync("events/orders", envelope);
if (publisher.LastEnvelope?.DeserializePayload<OrderCreated>()?.OrderId != 42
    || publisher.LastEnvelope.MessageType != "orders.created.v1")
    throw new InvalidOperationException("Messaging package consumer smoke check failed.");

var roundTripped = JsonSerializer.Deserialize<MessageEnvelope>(JsonSerializer.Serialize(envelope));
if (roundTripped?.MessageId != "event-42")
    throw new InvalidOperationException("Messaging envelope serialization consumer check failed.");

var dispatcher = new MessageDispatcher();
var handler = new ConsumerHandler();
dispatcher.Register("orders.created.v1", handler);
await dispatcher.DispatchAsync(envelope);
if (handler.LastOrderId != 42)
    throw new InvalidOperationException("Messaging handler dispatch consumer check failed.");

Console.WriteLine("Isolated PubSoft.Messaging consumer smoke check passed.");

sealed record OrderCreated(long OrderId);

sealed class ConsumerPublisher : IMessagePublisher
{
    public MessageEnvelope? LastEnvelope { get; private set; }

    public Task PublishAsync(string topic, MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (topic != "events/orders")
            throw new InvalidOperationException("Unexpected messaging topic.");
        LastEnvelope = envelope;
        return Task.CompletedTask;
    }
}

sealed class ConsumerHandler : IMessageHandler<OrderCreated>
{
    public long? LastOrderId { get; private set; }

    public Task HandleAsync(OrderCreated message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastOrderId = message.OrderId;
        return Task.CompletedTask;
    }
}