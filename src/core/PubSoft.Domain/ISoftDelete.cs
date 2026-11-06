namespace PubSoft.Domain;


/// <summary>Marker for entities that soft-delete instead of being removed.</summary>
public interface ISoftDelete
{
    /// <summary>Whether the row is deleted.</summary>
    bool IsDeleted { get; set; }
    /// <summary>Id of the user who deleted the row.</summary>
    long? DeleterId { get; set; }
    /// <summary>When the row was deleted.</summary>
    DateTime? DeletionTime { get; set; }
}
