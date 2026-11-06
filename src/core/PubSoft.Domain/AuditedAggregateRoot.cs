namespace PubSoft.Domain;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class AuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{

    protected AuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }
}
