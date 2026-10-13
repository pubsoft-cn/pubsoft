using System.Text.Json;
using PubSoft.Messaging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MessagingContractTests
{
    [Fact]
    public void EnvelopeRoundTripsPayloadAndPreservesExplicitMetadata()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var envelope = MessageEnvelope.Create(
            "018d1c2f-2e5d-7a00-8b91-58be80cbf300",
            "orders.order-created.v1",
            new OrderCreated(42, "accepted"),
            occurredAt,
            source: "orders",
            target: "billing");
        var json = JsonSerializer.Serialize(envelope);
        var roundTripped = JsonSerializer.Deserialize<MessageEnvelope>(json)!;

        Assert.Equal(envelope.MessageId, roundTripped.MessageId);
        Assert.Equal(envelope.MessageType, roundTripped.MessageType);
        Assert.Equal(occurredAt, roundTripped.OccurredAt);
        Assert.Equal("orders", roundTripped.Source);
        Assert.Equal("billing", roundTripped.Target);
        Assert.Equal(new OrderCreated(42, "accepted"), roundTripped.DeserializePayload<OrderCreated>());
    }

    [Fact]
    public void EnvelopeCopiesJsonPayloadAndDoesNotResolveRuntimeTypes()
    {
        using var document = JsonDocument.Parse("{\"Value\":7}");
        var envelope = new MessageEnvelope("id-1", "contract.order.v1", document.RootElement, DateTimeOffset.UtcNow);
        document.Dispose();

        Assert.Equal(7, envelope.Payload.GetProperty("Value").GetInt32());
        Assert.Equal(7, envelope.DeserializePayload<Payload>()!.Value);
    }

    [Theory]
    [InlineData("", "contract.v1")]
    [InlineData("id-1", " ")]
    [InlineData("bad\n", "contract.v1")]
    public void EnvelopeRejectsInvalidOpaqueIdentifiers(string messageId, string messageType)
    {
        using var document = JsonDocument.Parse("{}");

        Assert.Throws<ArgumentException>(() => new MessageEnvelope(
            messageId, messageType, document.RootElement, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task DispatcherUsesExplicitTypeRegistrationAndRunsHandlersInOrder()
    {
        var calls = new List<string>();
        var dispatcher = new MessageDispatcher();
        dispatcher.Register("orders.created.v1", new RecordingHandler("first", calls));
        dispatcher.Register("orders.created.v1", new RecordingHandler("second", calls));
        var envelope = MessageEnvelope.Create(
            "id-2", "orders.created.v1", new OrderCreated(12, "accepted"), DateTimeOffset.UtcNow);

        await dispatcher.DispatchAsync(envelope);

        Assert.Equal(["first:12", "second:12"], calls);
    }

    [Fact]
    public async Task DispatcherRejectsUnregisteredMessageType()
    {
        var dispatcher = new MessageDispatcher();
        var envelope = MessageEnvelope.Create(
            "id-3", "unregistered.event.v1", new OrderCreated(12, "accepted"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(envelope));
    }

    [Fact]
    public void DispatcherRejectsConflictingPayloadTypesForSameMessageKey()
    {
        var dispatcher = new MessageDispatcher();
        dispatcher.Register("orders.created.v1", new RecordingHandler("first", []));

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Register("orders.created.v1", new RecordingPayloadHandler()));
    }

    [Fact]
    public async Task DispatcherPropagatesCancellationBeforeInvokingHandlers()
    {
        var calls = new List<string>();
        var dispatcher = new MessageDispatcher();
        dispatcher.Register("orders.created.v1", new RecordingHandler("handler", calls));
        var envelope = MessageEnvelope.Create(
            "id-4", "orders.created.v1", new OrderCreated(12, "accepted"), DateTimeOffset.UtcNow);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(envelope, cancellation.Token));
        Assert.Empty(calls);
    }

    private sealed record OrderCreated(long OrderId, string Status);
    private sealed record Payload(int Value);

    private sealed class RecordingHandler(string name, ICollection<string> calls) : IMessageHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated message, CancellationToken cancellationToken = default)
        {
            calls.Add($"{name}:{message.OrderId}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPayloadHandler : IMessageHandler<Payload>
    {
        public Task HandleAsync(Payload message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}