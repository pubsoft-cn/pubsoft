namespace PubSoft.Domain;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class CreationAuditedAggregateRoot : AggregateRoot<long>, IMultiTenant
{

    protected CreationAuditedAggregateRoot() => CreationTime = DateTime.UtcNow;
    public long? TenantId { get; set; }
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
}
