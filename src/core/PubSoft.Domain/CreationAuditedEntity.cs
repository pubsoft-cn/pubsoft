namespace PubSoft.Domain;

/// <summary>Entity that records who created it and when.</summary>
[PubSoft.Core.BoundedCost("data object - timestamps assigned by infrastructure interceptors, not injected TimeProvider")]
public abstract class CreationAuditedEntity<TKey> : Entity<TKey>
{

    /// <summary>Stamps <see cref="CreationTime"/> at construction.</summary>
    protected CreationAuditedEntity() => CreationTime = DateTime.UtcNow;
    /// <summary>Id of the user who created the row.</summary>
    public long? CreatorId { get; set; }
    /// <summary>When the row was created (UTC).</summary>
    public DateTime CreationTime { get; set; }
}
