namespace PubSoft.Domain;

/// <summary>Entity that records creator and last modifier.</summary>
[PubSoft.Core.BoundedCost("data object - timestamps assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class AuditedEntity<TKey> : Entity<TKey>
{

    /// <summary>Stamps <see cref="CreationTime"/> at construction.</summary>
    protected AuditedEntity() => CreationTime = DateTime.UtcNow;
    /// <summary>Id of the user who created the row.</summary>
    public long? CreatorId { get; set; }
    /// <summary>When the row was created (UTC).</summary>
    public DateTime CreationTime { get; set; }
    /// <summary>Id of the user who last modified the row.</summary>
    public long? LastModifierId { get; set; }
    /// <summary>When the row was last modified (UTC).</summary>
    public DateTime? LastModificationTime { get; set; }
}
