namespace PubSoft.Domain;

/// <summary>A data-scope grant binding a role to ids within one dimension.</summary>
public sealed class RoleDataScope : Entity<long>, IMultiTenant
{

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    public RoleDataScope()
    {
    }

    /// <summary>Creates a data-scope entry for a role.</summary>
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
    /// <summary>The role receiving the scope.</summary>
    public long RoleId { get; set; }
    /// <summary>The dimension this scope restricts (registered via IDataScopeDimensionRegistry).</summary>
    public string Dimension { get; set; } = string.Empty;
    /// <summary>The id within the dimension the role may access.</summary>
    public long ScopeId { get; set; }
    /// <summary>Whether the role sees everything in this dimension.</summary>
    public bool IsAll { get; set; }
    /// <summary>Owning tenant; null for host-level rows.</summary>
    public long? TenantId { get; set; }
}

/// <summary>Resolves the data scopes granted to a subject through its roles.</summary>
public interface IRoleDataScopeProvider
{
    /// <summary>Returns the dimensions a subject may see.</summary>
    Task<IReadOnlyList<DataScopeDimension>> GetRoleScopesAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default);
}