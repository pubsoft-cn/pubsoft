using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;

/// <summary>Persistence seam for the outbox worker: batch claiming and lease-fenced completion marking.</summary>
public interface IOutboxStore
{
    /// <summary>Claims a due batch under per-message processing leases.</summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken = default);
    /// <summary>Marks the message delivered when <paramref name="leaseToken"/> still fences the row.</summary>
    Task<bool> MarkDeliveredAsync(long messageId, Guid leaseToken, CancellationToken cancellationToken = default);
    /// <summary>Schedules the retry or dead-letters the message when the lease still fences it.</summary>
    Task<bool> MarkFailedAsync(
        long messageId,
        Guid leaseToken,
        string errorCode,
        CancellationToken cancellationToken = default);
}
