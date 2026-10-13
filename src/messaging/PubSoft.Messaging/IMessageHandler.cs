namespace PubSoft.Messaging;

public interface IMessageHandler<in TMessage>
    where TMessage : class
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}