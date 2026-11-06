namespace PubSoft.Messaging;

/// <summary>Publishes messages to a topic transport.</summary>
public interface IMessagePublisher
{
    /// <summary>Publishes an envelope to a topic.</summary>
    Task PublishAsync(
        string topic,
        MessageEnvelope envelope,
        CancellationToken cancellationToken = default);
}