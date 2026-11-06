namespace PubSoft.Domain;

[PubSoft.Core.BoundedCost("domain entity — timestamps assigned by EF interceptors/domain services, not injected TimeProvider")]
public abstract class FullAuditedAggregateRoot : AggregateRoot<long>, IMultiTenant, ISoftDelete
{
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public bool IsDeleted { get; set; }
    public long? DeleterId { get; set; }
    public DateTime? DeletionTime { get; set; }

    protected FullAuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
}

[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class AuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }

    protected AuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
}

[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class CreationAuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }

    protected CreationAuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
}

[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class FullAuditedEntity<TKey> : Entity<TKey>, ISoftDelete
{
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public bool IsDeleted { get; set; }
    public long? DeleterId { get; set; }
    public DateTime? DeletionTime { get; set; }

    protected FullAuditedEntity() => CreationTime = DateTime.UtcNow;
}

[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class AuditedEntity<TKey> : Entity<TKey>
{
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }

    protected AuditedEntity() => CreationTime = DateTime.UtcNow;
}

[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class CreationAuditedEntity<TKey> : Entity<TKey>
{
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }

    protected CreationAuditedEntity() => CreationTime = DateTime.UtcNow;
}
