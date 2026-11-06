namespace PubSoft.MultiTenancy;

/// <summary>Tenant CRUD surface for hosts.</summary>
public interface ITenantAppService
{
    /// <summary>Lists tenants.</summary>
    Task<List<TenantDto>> GetListAsync();
    /// <summary>Gets one tenant.</summary>
    Task<TenantDto> GetAsync(long id);
    /// <summary>Creates a tenant.</summary>
    Task<TenantDto> CreateAsync(CreateTenantInput input);
    /// <summary>Updates a tenant.</summary>
    Task<TenantDto> UpdateAsync(long id, UpdateTenantInput input);
    /// <summary>Deletes a tenant.</summary>
    Task DeleteAsync(long id);
}
