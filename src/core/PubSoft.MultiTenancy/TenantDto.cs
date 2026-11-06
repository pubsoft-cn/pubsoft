namespace PubSoft.MultiTenancy;


/// <summary>Tenant read model.</summary>
public sealed class TenantDto
{
    /// <summary>Tenant id.</summary>
    public long Id { get; set; }
    /// <summary>Tenant code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Enabled flag.</summary>
    public bool IsEnabled { get; set; }
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    /// <summary>Creation time.</summary>
    public DateTime CreationTime { get; set; }
}
