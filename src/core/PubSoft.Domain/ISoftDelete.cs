namespace PubSoft.Domain;


public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    long? DeleterId { get; set; }
    DateTime? DeletionTime { get; set; }
}
