namespace PubSoft.Domain;

/// <summary>Aggregate root with full audit: tenant, creator, modifier, and soft-delete trail.</summary>
[PubSoft.Core.BoundedCost("data object - timestamps assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class FullAuditedAggregateRoot : AggregateRoot<long>, IMultiTenant, ISoftDelete
{

    /// <summary>Stamps <see cref="CreationTime"/> at construction.</summary>
    protected FullAuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
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
    /// <summary>Soft-delete marker - rows are hidden by query filters, not removed.</summary>
    public bool IsDeleted { get; set; }
    /// <summary>Id of the user who deleted the row.</summary>
    public long? DeleterId { get; set; }
    /// <summary>When the row was deleted (UTC).</summary>
    public DateTime? DeletionTime { get; set; }
}
