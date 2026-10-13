namespace PubSoft.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(
        string topic,
        MessageEnvelope envelope,
        CancellationToken cancellationToken = default);
}