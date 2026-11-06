namespace PubSoft.MultiTenancy;


/// <summary>Update payload for a tenant.</summary>
public sealed class UpdateTenantInput
{
    /// <summary>New name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>New display name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>New description.</summary>
    public string? Description { get; set; }
    /// <summary>Enabled flag.</summary>
    public bool IsEnabled { get; set; } = true;
}
