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

/// <summary>Writes manual business audit entries into the configured audit sink.</summary>
public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IAuditLogWriter"/> — adapts an <see cref="AuditLogEntry"/> into an
/// <see cref="AuditRequestSnapshot"/> (non-HTTP: method/path are null, status code is 0) and
/// forwards it to the resolved <see cref="IAuditSnapshotSink"/>.
/// </summary>
public sealed class AuditLogWriter(
    IAuditSnapshotSink sink,
    TimeProvider? timeProvider = null) : IAuditLogWriter
{
    private const int MaximumDetailLength = 16_384;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Business);

        var detail = entry.Metrics is { Count: > 0 }
            ? AppendMetrics(entry.Business.Detail, entry.Metrics)
            : entry.Business.Detail;

        var snapshot = new AuditRequestSnapshot(
            new AuditRequestContext(
                _timeProvider.GetUtcNow(),
                HttpMethod: null,
                RequestPath: null,
                Actor: entry.Actor),
            new AuditBusinessContext(
                entry.Business.Category,
                entry.Business.Action,
                entry.Business.TargetType,
                entry.Business.TargetId,
                entry.Business.TargetName,
                detail),
            entry.Success,
            StatusCode: 0,
            entry.Success ? null : entry.FailureCode ?? "OperationFailed",
            entry.Duration ?? TimeSpan.Zero,
            Changes: System.Array.Empty<AuditEntityChange>());

        await sink.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static string AppendMetrics(string? detail, IReadOnlyDictionary<string, string> metrics)
    {
        var rendered = string.Join(" ", metrics
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        var combined = string.IsNullOrEmpty(detail) ? rendered : $"{detail} | {rendered}";
        return combined.Length > MaximumDetailLength ? combined[..MaximumDetailLength] : combined;
    }
}
