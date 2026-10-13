using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public sealed class EfCoreRoleDataScopeProvider<TDbContext>(TDbContext dbContext) : IRoleDataScopeProvider
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task<IReadOnlyList<DataScopeDimension>> GetRoleScopesAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0 || tenantId is <= 0 || _dbContext.CurrentTenantId != tenantId)
            return [];

        var roleIds = await _dbContext.Set<SubjectPermissionRole>()
            .Where(assignment => assignment.TenantId == tenantId && assignment.SubjectId == subjectId)
            .Join(
                _dbContext.Set<PermissionRole>().Where(role => role.TenantId == tenantId && role.IsEnabled),
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Id)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var scopeRows = await _dbContext.Set<RoleDataScope>()
            .Where(scope => scope.TenantId == tenantId && roleIds.Contains(scope.RoleId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return scopeRows
            .Where(scope => !string.IsNullOrWhiteSpace(scope.Dimension)
                && scope.Dimension == scope.Dimension.Trim())
            .GroupBy(scope => scope.Dimension, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var isAll = group.Any(scope => scope.IsAll);
                var allowedIds = isAll
                    ? []
                    : group.Where(scope => scope.ScopeId > 0).Select(scope => scope.ScopeId).Distinct().ToArray();
                return new DataScopeDimension(group.Key, isAll, allowedIds);
            })
            .ToArray();
    }
}