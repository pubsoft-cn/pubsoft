using Microsoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;

/// <summary>
/// Explicitly invoked cleanup for <see cref="InboxReceipt"/> rows. The library never schedules
/// retention automatically; the host decides the retention window (sized to the maximum
/// redelivery/replay window) and the invocation cadence.
/// </summary>
/// <remarks>
/// Deletes run in bounded batches through EF tracking. <see cref="InboxReceipt"/> carries no query
/// filters -- scope is encoded in <see cref="InboxReceipt.TenantKey"/> -- so cleanup spans all rows
/// unless a tenant-key restriction is passed. Invoke from a host-scoped maintenance context.
/// Very large tables may prefer host-managed bulk SQL instead.
/// </remarks>
public sealed class InboxRetentionCleanup<TDbContext>
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public InboxRetentionCleanup(TDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Deletes receipts whose <see cref="InboxReceipt.ProcessedAt"/> is older than <paramref name="retention"/>.</summary>
    /// <param name="retention">How long processed receipts are kept before deletion.</param>
    /// <param name="batchSize">Maximum rows deleted per round trip.</param>
    /// <param name="tenantKey">
    /// Optional <see cref="InboxReceipt.TenantKey"/> restriction; <c>"host"</c> selects
    /// host-scope receipts. When null, receipts of every tenant are eligible.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of receipts deleted.</returns>
    public async Task<int> DeleteProcessedBeforeAsync(
        TimeSpan retention,
        int batchSize = 500,
        string? tenantKey = null,
        CancellationToken cancellationToken = default)
    {
        if (retention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retention));
        if (batchSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var cutoff = _timeProvider.GetUtcNow() - retention;
        var set = _dbContext.Set<InboxReceipt>();
        var total = 0;

        while (true)
        {
            var batch = await set
                .Where(receipt => receipt.ProcessedAt < cutoff)
                .Where(receipt => tenantKey == null || receipt.TenantKey == tenantKey) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
                .OrderBy(receipt => receipt.ProcessedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
                break;

            set.RemoveRange(batch);
            total += await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count < batchSize)
                break;
        }

        return total;
    }
}
