namespace PubSoft.Domain;

/// <summary>Aggregate root that records creator, last modifier, and tenant.</summary>
[PubSoft.Core.BoundedCost("data object - timestamps assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class AuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{

    /// <summary>Stamps <see cref="CreationTime"/> at construction.</summary>
    protected AuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
    /// <summary>Owning tenant; null for host-level rows.</summary>
    public long? TenantId { get; set; }
    /// <summary>Id of the user who created the row.</summary>
    public long? CreatorId { get; set; }
    /// <summary>When the row was created (UTC).</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Id of the user who last modified the row.</summary>
    public long? LastModifierId { get; set; }
    /// <summary>When the row was last modified (UTC).</summary>
    public DateTime? LastModificationTime { get; set; }
}
