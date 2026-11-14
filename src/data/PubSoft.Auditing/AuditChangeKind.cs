namespace PubSoft.Auditing;

/// <summary>Entity change classification.</summary>
public enum AuditChangeKind
{
    /// <summary>Row inserted.</summary>
    Created,
    /// <summary>Row modified.</summary>
    Updated,
    /// <summary>Row deleted.</summary>
    Deleted
}
