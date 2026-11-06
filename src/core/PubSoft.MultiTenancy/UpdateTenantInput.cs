namespace PubSoft.MultiTenancy;


public sealed class UpdateTenantInput
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
}
