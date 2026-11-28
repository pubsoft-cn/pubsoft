namespace PubSoft.Logging;

/// <summary>
/// Diagnostics snapshot of one sink's quarantine state. Dropped batches are counted so operators
/// can tell sink-local pauses from healthy silence; entry payloads never appear here.
/// </summary>
public sealed record LogSinkState
{
    /// <summary>Sink type name (low-cardinality diagnostics key).</summary>
    public required string SinkName { get; init; }

    /// <summary>Consecutive failed batches; resets on the first successful write.</summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>Whether the sink is currently paused.</summary>
    public required bool IsQuarantined { get; init; }

    /// <summary>End of the current quarantine pause, when quarantined.</summary>
    public required DateTimeOffset? QuarantinedUntil { get; init; }

    /// <summary>Batches skipped while quarantined.</summary>
    public required long SkippedBatches { get; init; }

    /// <summary>Total failed batches observed for this sink.</summary>
    public required long FailedBatches { get; init; }
}
