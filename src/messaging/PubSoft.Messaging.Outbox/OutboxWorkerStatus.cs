namespace PubSoft.Messaging.Outbox;

/// <summary>
/// Low-cardinality diagnostics snapshot of the outbox polling worker. Counts aggregate across
/// tenant scopes by design - no tenant keys, message ids, or payloads ever appear here.
/// </summary>
public sealed record OutboxWorkerStatus
{
    /// <summary>When the last poll iteration started, if any.</summary>
    public DateTimeOffset? LastPollStartedUtc { get; init; }

    /// <summary>When the last poll iteration completed (success or failure), if any.</summary>
    public DateTimeOffset? LastPollCompletedUtc { get; init; }

    /// <summary>Consecutive poll iterations that threw; resets on the first successful poll.</summary>
    public int ConsecutivePollFailures { get; init; }

    /// <summary>Exception type name of the last poll failure - type name only, never message text.</summary>
    public string? LastErrorType { get; init; }

    /// <summary>Claimed count of the most recent batch.</summary>
    public int LastBatchClaimedCount { get; init; }

    /// <summary>Delivered count of the most recent batch.</summary>
    public int LastBatchDeliveredCount { get; init; }

    /// <summary>Total poll iterations started.</summary>
    public long TotalPolls { get; init; }

    /// <summary>Total messages marked delivered since startup.</summary>
    public long TotalDelivered { get; init; }

    /// <summary>Total dispatch failures marked since startup.</summary>
    public long TotalDispatchFailures { get; init; }
}
