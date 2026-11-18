namespace PubSoft.Scheduling;

/// <summary>Options for a registered fenced worker.</summary>
public sealed class FencedWorkerOptions
{
    /// <summary>
    /// Distributed-lock key guarding this worker. Defaults to the worker registration name;
    /// hosts sharing a lock namespace across workers must set it explicitly.
    /// </summary>
    public string? LockKey { get; set; }
    /// <summary>Interval between ticks. Default 60s.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>
    /// Lease time on the lock. Must comfortably exceed the slowest expected tick -- an expiring
    /// lease mid-work is exactly what fencing tokens exist to defend against.
    /// Default 5 minutes.
    /// </summary>
    public TimeSpan LockExpiry { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>
    /// How long a tick waits for the lock. Default <see cref="TimeSpan.Zero"/> -- a held lock skips
    /// the tick entirely, which is the correct semantics for periodic sweep work.
    /// </summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.Zero;
    /// <summary>Whether the first tick runs immediately at startup. Default false -- waits one interval.</summary>
    public bool RunOnStartup { get; set; }
    /// <summary>Per-tick work timeout; an over-running tick is cancelled and retried next interval. Default 5 minutes.</summary>
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validate(string workerName)
    {
        if (string.IsNullOrWhiteSpace(LockKey) && string.IsNullOrWhiteSpace(workerName))
            throw new ArgumentException("Fenced worker requires a lock key or a non-empty worker name.", nameof(LockKey));
        if (Interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Interval), "Interval must be positive.");
        if (LockExpiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(LockExpiry), "Lock expiry must be positive.");
        if (WaitTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(WaitTimeout), "Wait timeout must be zero or positive.");
        if (MaxExecutionTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxExecutionTime), "Max execution time must be positive.");
    }
}
