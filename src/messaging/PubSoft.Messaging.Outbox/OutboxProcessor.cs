using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


/// <summary>Poll worker: claims leased batches, dispatches each, and marks the outcome.</summary>
public sealed class OutboxProcessor(
    IOutboxStore store,
    IOutboxDispatcher dispatcher,
    ILogger<OutboxProcessor>? logger = null)
{
    /// <summary>Claims and dispatches one batch; returns the delivered count.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await store.ClaimBatchAsync(cancellationToken).ConfigureAwait(false);
        var delivered = 0;
        foreach (var message in messages)
        {
            var leaseToken = message.LeaseToken
                ?? throw new InvalidOperationException("Claimed outbox messages must have a lease token.");
            try
            {
                await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                if (await store.MarkDeliveredAsync(message.Id, leaseToken, cancellationToken).ConfigureAwait(false))
                    delivered++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var errorCode = exception.GetType().Name;
                await store.MarkFailedAsync(message.Id, leaseToken, errorCode, cancellationToken).ConfigureAwait(false);
                logger?.LogWarning("Outbox dispatch failed ({ExceptionType}).", errorCode);
            }
        }

        return delivered;
    }
}
