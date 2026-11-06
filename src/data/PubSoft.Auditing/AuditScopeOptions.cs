using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public sealed class AuditScopeOptions
{
    public bool Enabled { get; set; } = true;
    public bool AuditGetRequests { get; set; }
}
