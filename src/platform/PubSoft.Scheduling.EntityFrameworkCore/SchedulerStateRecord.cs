using PubSoft.Domain;

namespace PubSoft.Scheduling.EntityFrameworkCore;

/// <summary>EF persistence record for scheduler state (locks, checkpoints) keyed by tenant scope + hashed key.</summary>
public sealed class SchedulerStateRecord : IMultiTenant
{
    /// <summary>Snowflake primary key; application-assigned.</summary>
    public long Id { get; set; }
    /// <summary>Tenant partition; null for host-scope state.</summary>
    public long? TenantId { get; set; }
    /// <summary>Tenant discriminator segment of the state key.</summary>
    public string TenantScope { get; set; } = string.Empty;
    /// <summary>Logical state key (unbounded length; indexed via <see cref="KeyHash"/>).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>SHA-256 hex of <see cref="Key"/> - the indexed lookup column.</summary>
    public string KeyHash { get; set; } = string.Empty;
    /// <summary>Serialized state payload.</summary>
    public string ValueJson { get; set; } = string.Empty;
    /// <summary>Last write timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Optional expiry for retention cleanup.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}