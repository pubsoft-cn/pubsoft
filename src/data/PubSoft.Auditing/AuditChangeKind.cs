using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;

public enum AuditChangeKind
{
    Created,
    Updated,
    Deleted
}
