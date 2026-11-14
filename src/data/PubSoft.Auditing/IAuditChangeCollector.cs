namespace PubSoft.Auditing;


/// <summary>Accumulates entity changes for the current scope.</summary>
public interface IAuditChangeCollector
{
    /// <summary>Returns the changes captured so far.</summary>
    IReadOnlyList<AuditEntityChange> GetSnapshot();
    /// <summary>Adds one entity change.</summary>
    void Add(AuditEntityChange change);
    /// <summary>Adds multiple entity changes.</summary>
    void AddRange(IReadOnlyCollection<AuditEntityChange> changes);
    /// <summary>Drops all captured changes.</summary>
    void Clear();
}
