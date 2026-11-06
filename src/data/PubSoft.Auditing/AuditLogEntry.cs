namespace PubSoft.Auditing;

/// <summary>
/// A business-meaning audit entry written outside an HTTP request scope — "who did what to which
/// target" for service-level operations (login attempts, admin actions, external integrations).
/// Shares the same <see cref="IAuditSnapshotSink"/> pipeline as middleware-captured entries, so
/// manual entries get identical persistence, retention, and archiving.
/// </summary>
public sealed record AuditLogEntry(
    AuditBusinessContext Business,
    AuditActorContext? Actor = null,
    bool Success = true,
    string? FailureCode = null,
    TimeSpan? Duration = null,
    IReadOnlyDictionary<string, string>? Metrics = null);
