using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Authorization;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>EF-backed permission-grant provider.</summary>
public sealed class EfCorePermissionGrantProvider<TDbContext>(TDbContext dbContext) : IPermissionGrantProvider
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    /// <inheritdoc />
    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default)
        => await GetGrantsAsync(subjectId, tenantId, string.Empty, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || clientCode != clientCode.Trim()) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
        if (subjectId <= 0 || tenantId is <= 0 || _dbContext.CurrentTenantId != tenantId)
            return [];

        var roleIds = await _dbContext.Set<SubjectPermissionRole>()
            .Where(assignment => assignment.TenantId == tenantId
                && assignment.ClientCode == clientCode // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
                && assignment.SubjectId == subjectId)
            .Join(
                _dbContext.Set<PermissionRole>().Where(role => role.TenantId == tenantId
                    && role.ClientCode == clientCode && role.IsEnabled), // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Id)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var grantRows = await _dbContext.Set<PermissionGrantRecord>()
            .Where(grant => grant.TenantId == tenantId && roleIds.Contains(grant.RoleId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return grantRows.Select(grant => new PermissionGrant(
                grant.TenantId,
                grant.ModuleKey,
                (PermissionFlags)grant.Flags,
                grant.ExtensionFlags,
                grant.CustomPermissionName is null ? null : [grant.CustomPermissionName],
                clientCode))
            .ToArray();
    }
}