namespace PubSoft.Messaging;

/// <summary>Handles one message type.</summary>
public interface IMessageHandler<in TMessage>
    where TMessage : class
{
    /// <summary>Handles the message.</summary>
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}