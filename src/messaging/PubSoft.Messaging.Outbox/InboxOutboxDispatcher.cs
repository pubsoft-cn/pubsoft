namespace PubSoft.Messaging.Outbox;


/// <summary>Dispatcher base that wraps delivery in inbox dedup: the consumer handler runs once per message.</summary>
public abstract class InboxOutboxDispatcher(IInboxMessageProcessor inboxProcessor) : IOutboxDispatcher
{
    private readonly IInboxMessageProcessor _inboxProcessor = inboxProcessor
        ?? throw new ArgumentNullException(nameof(inboxProcessor));

    /// <summary>Dedup identity of the consuming component.</summary>
    protected abstract string ConsumerName { get; }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _inboxProcessor.ProcessOnceAsync(
            ConsumerName,
            message.Id,
            token => HandleMessageAsync(message, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Actual delivery; runs only when inbox dedup admits the message.</summary>
    protected abstract Task HandleMessageAsync(OutboxMessage message, CancellationToken cancellationToken);
}
