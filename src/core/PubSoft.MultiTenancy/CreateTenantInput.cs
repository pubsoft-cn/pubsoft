namespace PubSoft.MultiTenancy;


/// <summary>Create payload for a tenant.</summary>
public sealed class CreateTenantInput
{
    /// <summary>Tenant code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Description.</summary>
    public string? Description { get; set; }
}
