namespace PubSoft.Domain;

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

public abstract class AuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }

    protected AuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
}

public abstract class CreationAuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }

    protected CreationAuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
}

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

public abstract class AuditedEntity<TKey> : Entity<TKey>
{
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }

    protected AuditedEntity() => CreationTime = DateTime.UtcNow;
}

public abstract class CreationAuditedEntity<TKey> : Entity<TKey>
{
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }

    protected CreationAuditedEntity() => CreationTime = DateTime.UtcNow;
}
