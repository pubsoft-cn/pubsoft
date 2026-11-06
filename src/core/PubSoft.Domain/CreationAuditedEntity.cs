namespace PubSoft.Domain;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class CreationAuditedEntity<TKey> : Entity<TKey>
{

    protected CreationAuditedEntity() => CreationTime = DateTime.UtcNow;
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
}
