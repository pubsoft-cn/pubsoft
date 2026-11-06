namespace PubSoft.Domain;

/// <summary>Marker for tenant-owned rows - drives tenant query filtering.</summary>
public interface IMultiTenant
{
    /// <summary>Owning tenant; null for host-level rows.</summary>
    long? TenantId { get; set; }
}
