using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public abstract class InboxOutboxDispatcher(IInboxMessageProcessor inboxProcessor) : IOutboxDispatcher
{
    private readonly IInboxMessageProcessor _inboxProcessor = inboxProcessor
        ?? throw new ArgumentNullException(nameof(inboxProcessor));

    protected abstract string ConsumerName { get; }

    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _inboxProcessor.ProcessOnceAsync(
            ConsumerName,
            message.Id,
            token => HandleMessageAsync(message, token),
            cancellationToken).ConfigureAwait(false);
    }

    protected abstract Task HandleMessageAsync(OutboxMessage message, CancellationToken cancellationToken);
}
