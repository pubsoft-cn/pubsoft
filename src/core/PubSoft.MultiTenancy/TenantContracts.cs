namespace PubSoft.MultiTenancy;

public sealed class TenantDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
}

public sealed class CreateTenantInput
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

public sealed class UpdateTenantInput
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public interface ITenantAppService
{
    Task<List<TenantDto>> GetListAsync();
    Task<TenantDto> GetAsync(long id);
    Task<TenantDto> CreateAsync(CreateTenantInput input);
    Task<TenantDto> UpdateAsync(long id, UpdateTenantInput input);
    Task DeleteAsync(long id);
}