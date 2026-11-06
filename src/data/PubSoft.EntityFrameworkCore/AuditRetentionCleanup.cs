using Microsoft.EntityFrameworkCore;

namespace PubSoft.EntityFrameworkCore.Auditing;

/// <summary>
/// Explicitly invoked retention cleanup for <see cref="AuditLogRecord"/> rows. The library never
/// schedules cleanup automatically; the host owns the retention policy and invocation cadence.
/// </summary>
/// <remarks>
/// Deletes run in bounded batches through EF tracking; entity-change rows are removed by the
/// configured cascade foreign key, which requires the mapping's cascade relationship to exist in
/// the database. <see cref="AuditLogRecord"/> carries no query filters, so cleanup spans all rows
/// unless the <see cref="AuditLogRecord.TenantId"/>-restricted overload is used. Invoke from a
/// host-scoped maintenance context.
/// </remarks>
public sealed class AuditRetentionCleanup<TDbContext>
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public AuditRetentionCleanup(TDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Deletes audit logs whose <see cref="AuditLogRecord.StartedAt"/> is older than <paramref name="retention"/>, across all tenants.</summary>
    /// <returns>Total number of audit log rows deleted.</returns>
    public Task<int> DeleteStartedBeforeAsync(
        TimeSpan retention,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
        => DeleteAsync(retention, batchSize, tenantId: null, restrictToTenant: false, cancellationToken);

    /// <summary>Deletes audit logs older than <paramref name="retention"/> restricted to one <see cref="AuditLogRecord.TenantId"/> (<c>null</c> selects host-scope rows).</summary>
    /// <returns>Total number of audit log rows deleted.</returns>
    public Task<int> DeleteStartedBeforeAsync(
        TimeSpan retention,
        long? tenantId,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
        => DeleteAsync(retention, batchSize, tenantId, restrictToTenant: true, cancellationToken);

    /// <summary>
    /// Archives audit logs older than <paramref name="retention"/> by handing each expired batch —
    /// including its <see cref="AuditLogRecord.EntityChanges"/> — to <paramref name="exportAsync"/>
    /// before deleting it. Across all tenants.
    /// </summary>
    /// <remarks>
    /// If <paramref name="exportAsync"/> writes to the same <typeparamref name="TDbContext"/>
    /// (e.g. inserting into a history table without calling SaveChanges), the delete
    /// <c>SaveChangesAsync</c> commits the archive inserts and the deletes atomically.
    /// For any other destination the export completes before the delete (at-least-once archive);
    /// an export failure stops the run without deleting the batch.
    /// </remarks>
    /// <returns>Total number of audit log rows deleted after successful export.</returns>
    public Task<int> ArchiveStartedBeforeAsync(
        TimeSpan retention,
        Func<IReadOnlyList<AuditLogRecord>, CancellationToken, Task> exportAsync,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
        => ArchiveAsync(retention, exportAsync, batchSize, tenantId: null, restrictToTenant: false, cancellationToken);

    /// <summary>Archives audit logs older than <paramref name="retention"/> restricted to one <see cref="AuditLogRecord.TenantId"/> (<c>null</c> selects host-scope rows).</summary>
    /// <inheritdoc cref="ArchiveStartedBeforeAsync(TimeSpan, Func{IReadOnlyList{AuditLogRecord}, CancellationToken, Task}, int, CancellationToken)"/>
    public Task<int> ArchiveStartedBeforeAsync(
        TimeSpan retention,
        long? tenantId,
        Func<IReadOnlyList<AuditLogRecord>, CancellationToken, Task> exportAsync,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
        => ArchiveAsync(retention, exportAsync, batchSize, tenantId, restrictToTenant: true, cancellationToken);

    private async Task<int> ArchiveAsync(
        TimeSpan retention,
        Func<IReadOnlyList<AuditLogRecord>, CancellationToken, Task> exportAsync,
        int batchSize,
        long? tenantId,
        bool restrictToTenant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exportAsync);
        if (retention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retention));
        if (batchSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var cutoff = _timeProvider.GetUtcNow() - retention;
        var set = _dbContext.Set<AuditLogRecord>();
        var total = 0;

        while (true)
        {
            var batch = await set
                .Include(record => record.EntityChanges)
                .Where(record => record.StartedAt < cutoff)
                .Where(record => !restrictToTenant || record.TenantId == tenantId)
                .OrderBy(record => record.StartedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
                break;

            await exportAsync(batch, cancellationToken).ConfigureAwait(false);
            set.RemoveRange(batch);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            total += batch.Count;
            if (batch.Count < batchSize)
                break;
        }

        return total;
    }

    private async Task<int> DeleteAsync(
        TimeSpan retention,
        int batchSize,
        long? tenantId,
        bool restrictToTenant,
        CancellationToken cancellationToken)
    {
        if (retention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retention));
        if (batchSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var cutoff = _timeProvider.GetUtcNow() - retention;
        var set = _dbContext.Set<AuditLogRecord>();
        var total = 0;

        while (true)
        {
            var batch = await set
                .Where(record => record.StartedAt < cutoff)
                .Where(record => !restrictToTenant || record.TenantId == tenantId)
                .OrderBy(record => record.StartedAt)
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
