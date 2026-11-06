using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


/// <summary>Per-scope audit toggles.</summary>
public sealed class AuditScopeOptions
{
    /// <summary>Whether the scope audits at all.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Whether GET requests are audited (default: no - read-only noise).</summary>
    public bool AuditGetRequests { get; set; }
}
