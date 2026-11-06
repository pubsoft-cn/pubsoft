namespace PubSoft.Domain;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class AuditedEntity<TKey> : Entity<TKey>
{

    protected AuditedEntity() => CreationTime = DateTime.UtcNow;
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }
}
