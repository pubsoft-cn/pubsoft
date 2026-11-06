using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


/// <summary>Immutable audit record for one request: context, outcome, and entity changes.</summary>
/// <param name="Request">Request facts (method, path, IP, actor).</param>
/// <param name="Business">Business-operation metadata.</param>
/// <param name="Success">Whether the request succeeded.</param>
/// <param name="StatusCode">HTTP status or 0 for non-HTTP entries.</param>
/// <param name="FailureCode">Machine-readable failure code.</param>
/// <param name="Duration">Request duration.</param>
/// <param name="Changes">Captured entity changes.</param>
public sealed record AuditRequestSnapshot(
    AuditRequestContext Request,
    AuditBusinessContext Business,
    bool Success,
    int StatusCode,
    string? FailureCode,
    TimeSpan Duration,
    IReadOnlyList<AuditEntityChange> Changes);
