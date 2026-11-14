namespace PubSoft.Auditing;


/// <summary>Mutable per-request audit scope; finalized into an immutable snapshot.</summary>
public interface IAuditScope
{
    /// <summary>Whether auditing is active for this scope.</summary>
    bool IsEnabled { get; }
    /// <summary>Whether the scope is currently finalizing (suppresses nested writes).</summary>
    bool IsFinalizing { get; }

    /// <summary>Captures the request facts once; later calls are ignored.</summary>
    void Initialize(AuditRequestContext request);
    /// <summary>Attaches business-operation metadata.</summary>
    void SetBusiness(
        string? category = null,
        string? action = null,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        string? detail = null);
    /// <summary>Marks the scope as failed with an optional code.</summary>
    void MarkFailure(string? failureCode = null);
    /// <summary>Freezes the scope into a snapshot; null when disabled.</summary>
    AuditRequestSnapshot? Finalize(bool success, int statusCode);
}
