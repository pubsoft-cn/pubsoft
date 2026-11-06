namespace PubSoft.Domain;


/// <summary>Ambient tenant for the current flow - resolved per request/job by the host.</summary>
public interface ITenantContext
{
    /// <summary>The current tenant; null for host (unscoped) context.</summary>
    long? TenantId { get; }
}
