namespace PubSoft.Domain;


[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class FullAuditedEntity<TKey> : Entity<TKey>, ISoftDelete
{

    protected FullAuditedEntity() => CreationTime = DateTime.UtcNow;
    public long? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public long? LastModifierId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public bool IsDeleted { get; set; }
    public long? DeleterId { get; set; }
    public DateTime? DeletionTime { get; set; }
}
