namespace PubSoft.Domain;

public sealed class RoleDataScope : Entity<long>, IMultiTenant
{

    public RoleDataScope()
    {
    }

    public RoleDataScope(
        long id,
        long roleId,
        string dimension,
        long scopeId,
        long? tenantId = null,
        bool isAll = false)
    {
        Id = id;
        RoleId = roleId;
        Dimension = dimension;
        ScopeId = scopeId;
        TenantId = tenantId;
        IsAll = isAll;
    }
    public long RoleId { get; set; }
    public string Dimension { get; set; } = string.Empty;
    public long ScopeId { get; set; }
    public bool IsAll { get; set; }
    public long? TenantId { get; set; }
}

public interface IRoleDataScopeProvider
{
    Task<IReadOnlyList<DataScopeDimension>> GetRoleScopesAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default);
}