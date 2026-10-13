using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public sealed class EfCorePermissionGrantManager<TDbContext>(TDbContext dbContext)
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task ReplaceRoleGrantsAsync(
        long roleId,
        long? tenantId,
        IEnumerable<PermissionGrantRecord> grants,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(tenantId);
        if (roleId <= 0)
            throw new ArgumentOutOfRangeException(nameof(roleId));
        ArgumentNullException.ThrowIfNull(grants);
        var snapshot = grants.ToArray();
        ValidateGrantIds(snapshot, nameof(grants));
        if (snapshot.Any(grant => grant.RoleId != roleId || grant.TenantId != tenantId))
            throw new ArgumentException("Every grant must target the specified role and tenant.", nameof(grants));

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var roleExists = await _dbContext.Set<PermissionRole>()
            .AnyAsync(role => role.Id == roleId && role.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!roleExists)
            throw new KeyNotFoundException("Permission role was not found in the current tenant.");

        var existing = await _dbContext.Set<PermissionGrantRecord>()
            .Where(grant => grant.RoleId == roleId && grant.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        ApplyGrantReplacement(existing, snapshot);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AssignSubjectRolesAsync(
        long subjectId,
        long? tenantId,
        IEnumerable<long> roleIds,
        CancellationToken cancellationToken = default)
        => await AssignSubjectRolesAsync(subjectId, tenantId, string.Empty, roleIds, cancellationToken)
            .ConfigureAwait(false);

    public async Task AssignSubjectRolesAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        IEnumerable<long> roleIds,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(tenantId);
        ValidateClientCode(clientCode);
        if (subjectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(subjectId));
        ArgumentNullException.ThrowIfNull(roleIds);
        var roleIdSnapshot = roleIds.Distinct().ToArray();
        if (roleIdSnapshot.Any(roleId => roleId <= 0))
            throw new ArgumentOutOfRangeException(nameof(roleIds), "Role IDs must be positive.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var enabledRoleCount = await _dbContext.Set<PermissionRole>()
            .CountAsync(role => role.TenantId == tenantId && role.ClientCode == clientCode
                && role.IsEnabled && roleIdSnapshot.Contains(role.Id), cancellationToken)
            .ConfigureAwait(false);
        if (enabledRoleCount != roleIdSnapshot.Length)
            throw new InvalidOperationException("All assigned roles must be enabled and belong to the current tenant.");

        var existing = await _dbContext.Set<SubjectPermissionRole>()
            .Where(assignment => assignment.TenantId == tenantId
                && assignment.ClientCode == clientCode && assignment.SubjectId == subjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _dbContext.RemoveRange(existing);
        _dbContext.AddRange(roleIdSnapshot.Select(roleId => new SubjectPermissionRole
        {
            SubjectId = subjectId,
            RoleId = roleId,
            TenantId = tenantId,
            ClientCode = clientCode
        }));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ValidateScope(long? tenantId)
    {
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        if (_dbContext.CurrentTenantId != tenantId)
            throw new InvalidOperationException("Permission writes must use the matching tenant context.");
    }

    private static void ValidateClientCode(string clientCode)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || clientCode != clientCode.Trim())
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
    }

    private void ApplyGrantReplacement(
        IReadOnlyCollection<PermissionGrantRecord> existing,
        IReadOnlyCollection<PermissionGrantRecord> desired)
    {
        var remaining = desired.ToDictionary(grant => grant.Id);
        foreach (var current in existing)
        {
            if (!remaining.Remove(current.Id, out var replacement))
            {
                _dbContext.Remove(current);
                continue;
            }

            current.ModuleKey = replacement.ModuleKey;
            current.SetPermissions(
                replacement.Flags,
                replacement.ExtensionFlags,
                replacement.CustomPermissionName);
        }

        _dbContext.AddRange(remaining.Values);
    }

    private static void ValidateGrantIds(IReadOnlyCollection<PermissionGrantRecord> grants, string parameterName)
    {
        if (grants.Any(grant => grant.Id <= 0))
            throw new ArgumentException("Grant record IDs must be positive.", parameterName);
        if (grants.Select(grant => grant.Id).Distinct().Count() != grants.Count)
            throw new ArgumentException("Grant record IDs must be unique.", parameterName);
    }
}