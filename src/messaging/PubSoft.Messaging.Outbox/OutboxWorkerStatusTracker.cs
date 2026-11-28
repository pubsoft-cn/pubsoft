namespace PubSoft.Messaging.Outbox;

/// <summary>
/// Thread-safe recorder behind <see cref="OutboxWorkerStatus"/> snapshots. Registered as a singleton
/// by the outbox DI extensions; processor and worker report into it, hosts read snapshots for
/// health/metrics wiring.
/// </summary>
public sealed class OutboxWorkerStatusTracker
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _lastPollStartedUtc;
    private DateTimeOffset? _lastPollCompletedUtc;
    private int _consecutivePollFailures;
    private string? _lastErrorType;
    private int _lastBatchClaimedCount;
    private int _lastBatchDeliveredCount;
    private long _totalPolls;
    private long _totalDelivered;
    private long _totalDispatchFailures;

    /// <summary>Creates the tracker; <paramref name="timeProvider"/> is injectable for tests.</summary>
    public OutboxWorkerStatusTracker(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Marks the start of a poll iteration.</summary>
    public void RecordPollStarted()
    {
        lock (_sync)
        {
            _lastPollStartedUtc = _timeProvider.GetUtcNow();
            _totalPolls++;
        }
    }

    /// <summary>Marks a batch outcome within a poll iteration. Empty polls are not batches - the processor skips them so the last-batch counters keep describing the most recent non-empty claim.</summary>
    public void RecordBatchCompleted(int claimedCount, int deliveredCount, int dispatchFailureCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(claimedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(deliveredCount);
        ArgumentOutOfRangeException.ThrowIfNegative(dispatchFailureCount);
        lock (_sync)
        {
            _lastBatchClaimedCount = claimedCount;
            _lastBatchDeliveredCount = deliveredCount;
            _totalDelivered += deliveredCount;
            _totalDispatchFailures += dispatchFailureCount;
        }
    }

    /// <summary>Marks a poll iteration as completed without an iteration-level failure.</summary>
    public void RecordPollSucceeded()
    {
        lock (_sync)
        {
            _lastPollCompletedUtc = _timeProvider.GetUtcNow();
            _consecutivePollFailures = 0;
        }
    }

    /// <summary>Marks a poll iteration as failed; <paramref name="errorType"/> is the exception type name only.</summary>
    public void RecordPollFailure(string errorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorType);
        lock (_sync)
        {
            _lastPollCompletedUtc = _timeProvider.GetUtcNow();
            _consecutivePollFailures++;
            _lastErrorType = errorType;
        }
    }

    /// <summary>Point-in-time snapshot; mutable state is copied under the lock.</summary>
    public OutboxWorkerStatus Snapshot()
    {
        lock (_sync)
        {
            return new OutboxWorkerStatus
            {
                LastPollStartedUtc = _lastPollStartedUtc,
                LastPollCompletedUtc = _lastPollCompletedUtc,
                ConsecutivePollFailures = _consecutivePollFailures,
                LastErrorType = _lastErrorType,
                LastBatchClaimedCount = _lastBatchClaimedCount,
                LastBatchDeliveredCount = _lastBatchDeliveredCount,
                TotalPolls = _totalPolls,
                TotalDelivered = _totalDelivered,
                TotalDispatchFailures = _totalDispatchFailures,
            };
        }
    }
}
