using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public sealed record AuditRequestSnapshot(
    AuditRequestContext Request,
    AuditBusinessContext Business,
    bool Success,
    int StatusCode,
    string? FailureCode,
    TimeSpan Duration,
    IReadOnlyList<AuditEntityChange> Changes);
