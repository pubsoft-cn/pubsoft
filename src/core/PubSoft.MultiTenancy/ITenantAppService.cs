namespace PubSoft.MultiTenancy;

public interface ITenantAppService
{
    Task<List<TenantDto>> GetListAsync();
    Task<TenantDto> GetAsync(long id);
    Task<TenantDto> CreateAsync(CreateTenantInput input);
    Task<TenantDto> UpdateAsync(long id, UpdateTenantInput input);
    Task DeleteAsync(long id);
}
