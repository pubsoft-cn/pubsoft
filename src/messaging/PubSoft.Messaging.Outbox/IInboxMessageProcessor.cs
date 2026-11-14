namespace PubSoft.Messaging.Outbox;


/// <summary>Inbox dedup seam: runs the handler once per (consumer, messageId) and records a receipt.</summary>
public interface IInboxMessageProcessor
{
    /// <summary>Returns false when the message was already processed; otherwise runs <paramref name="handler"/> and records the receipt.</summary>
    Task<bool> ProcessOnceAsync(
        string consumerName,
        long messageId,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}
