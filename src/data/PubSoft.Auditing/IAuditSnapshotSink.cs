namespace PubSoft.Auditing;


/// <summary>Persists finalized audit snapshots (EF store, outbox, or host-provided).</summary>
public interface IAuditSnapshotSink
{
    /// <summary>Writes one snapshot.</summary>
    ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken cancellationToken = default);
}
