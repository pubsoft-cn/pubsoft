using Microsoft.EntityFrameworkCore;
using PubSoft.Core;

namespace PubSoft.EntityFrameworkCore.Auditing;

/// <summary>Read-side query service over persisted <see cref="AuditLogRecord"/> rows -- migration surface for the legacy AuditLogAppService.</summary>
public class AuditLogQueryService<TDbContext>(TDbContext dbContext) where TDbContext : PubSoftDbContext
{
    /// <summary>Paged audit-log query; tenant-scoped to the ambient context (host sees all tenants).</summary>
    public async Task<PagedResultDto<AuditLogRecord>> GetListAsync(
        string? entityType = null,
        string? subjectId = null,
        bool? success = null,
        DateTimeOffset? startedAfter = null,
        DateTimeOffset? startedBefore = null,
        string? category = null,
        string? action = null,
        string? targetType = null,
        string? keyword = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);

        var q = dbContext.Set<AuditLogRecord>().AsNoTracking();
        var tid = dbContext.CurrentTenantId;
        // Host context (tid=null, platform admin) lifts the tenant filter - sees every tenant's rows.
        if (tid.HasValue) q = q.Where(a => a.TenantId == tid);
        else q = q.IgnoreScopeFilters();

        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(a => a.EntityChanges.Any(e => e.EntityType == entityType.Trim())); // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
        if (!string.IsNullOrWhiteSpace(subjectId))
            q = q.Where(a => a.SubjectId == subjectId.Trim()); // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
        if (success.HasValue)
            q = q.Where(a => a.Success == success.Value);
        if (startedAfter.HasValue)
            q = q.Where(a => a.StartedAt >= startedAfter.Value);
        if (startedBefore.HasValue)
            q = q.Where(a => a.StartedAt <= startedBefore.Value);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(a => a.Category == category.Trim()); // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(a => a.Action == action.Trim()); // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
        if (!string.IsNullOrWhiteSpace(targetType))
            q = q.Where(a => a.TargetType == targetType.Trim()); // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(a => (a.RequestPath != null && a.RequestPath.Contains(kw))
                || (a.TargetName != null && a.TargetName.Contains(kw))
                || (a.Detail != null && a.Detail.Contains(kw)));
        }

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await q.OrderByDescending(a => a.StartedAt).Skip(skip).Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<AuditLogRecord> { Items = items, TotalCount = total };
    }
}
