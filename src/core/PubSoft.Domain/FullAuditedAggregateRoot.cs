namespace PubSoft.Domain;

[PubSoft.Core.BoundedCost("domain entity — timestamps assigned by EF interceptors/domain services, not injected TimeProvider")]
public abstract class FullAuditedAggregateRoot : AggregateRoot<long>, IMultiTenant, ISoftDelete
{

    protected FullAuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public bool IsDeleted { get; set; }
    public long? DeleterId { get; set; }
    public DateTime? DeletionTime { get; set; }
}
