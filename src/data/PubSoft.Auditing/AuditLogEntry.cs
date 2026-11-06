namespace PubSoft.Auditing;

/// <summary>
/// A business-meaning audit entry written outside an HTTP request scope -- "who did what to which
/// target" for service-level operations (login attempts, admin actions, external integrations).
/// Shares the same <see cref="IAuditSnapshotSink"/> pipeline as middleware-captured entries, so
/// manual entries get identical persistence, retention, and archiving.
/// </summary>
/// <param name="Business">Business-operation metadata.</param>
/// <param name="Actor">Acting subject.</param>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="FailureCode">Machine-readable failure code.</param>
/// <param name="Duration">Operation duration.</param>
/// <param name="Metrics">Numeric metrics folded into the detail.</param>
public sealed record AuditLogEntry(
    AuditBusinessContext Business,
    AuditActorContext? Actor = null,
    bool Success = true,
    string? FailureCode = null,
    TimeSpan? Duration = null,
    IReadOnlyDictionary<string, string>? Metrics = null);
