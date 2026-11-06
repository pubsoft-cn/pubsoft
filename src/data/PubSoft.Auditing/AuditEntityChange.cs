using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


public sealed record AuditEntityChange(
    string EntityType,
    string EntityId,
    AuditChangeKind Kind,
    DateTimeOffset ChangedAt,
    IReadOnlyList<AuditPropertyChange> Properties);
