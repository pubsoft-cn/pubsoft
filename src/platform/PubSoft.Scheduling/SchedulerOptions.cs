namespace PubSoft.Scheduling;

/// <summary>Scheduler runtime options.</summary>
public sealed class SchedulerOptions
{
    /// <summary>Configured jobs.</summary>
    public List<ScheduledJob> Jobs { get; set; } = [];
    /// <summary>Tick interval between due-job checks.</summary>
    public TimeSpan LoopInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Delay after a tick-level failure.</summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Concurrent job cap per tick.</summary>
    public int MaxConcurrentJobs { get; set; } = 16;

    /// <summary>
    /// Guard every job with a shared <see cref="PubSoft.Caching.IDistributedLock"/> so only one
    /// instance runs a given job per tick. Fail-closed: enabled without a registered lock skips jobs.
    /// </summary>
    public bool DistributedLocking { get; set; }
    /// <summary>Lease held per job tick; should exceed the longest expected job run.</summary>
    public TimeSpan DistributedLockLease { get; set; } = TimeSpan.FromMinutes(5);

    internal bool IsValid(out string error)
    {
        if (Jobs is null)
        {
            error = "Jobs cannot be null.";
            return false;
        }
        if (LoopInterval <= TimeSpan.Zero || ErrorDelay <= TimeSpan.Zero)
        {
            error = "Scheduler delays must be greater than zero.";
            return false;
        }
        if (MaxConcurrentJobs <= 0)
        {
            error = "MaxConcurrentJobs must be greater than zero.";
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in Jobs)
        {
            if (job is null || string.IsNullOrWhiteSpace(job.Name)
                || string.IsNullOrWhiteSpace(job.ExecutorType) || !job.Schedule.IsValid
                || job.ResultProcessors is null || job.ResultProcessors.Any(string.IsNullOrWhiteSpace))
            {
                error = "Each job requires a name, executor type, valid schedule, and valid processor names.";
                return false;
            }
            if (!names.Add(job.Name))
            {
                error = $"Job name '{job.Name}' is duplicated.";
                return false;
            }
            if (job.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            {
                error = $"Job '{job.Name}' Timeout must be greater than zero.";
                return false;
            }
            if (job.Retry is { } retry
                && (retry.MaxAttempts < 1 || retry.InitialDelay < TimeSpan.Zero
                    || retry.BackoffFactor < 1.0 || retry.MaxDelay < TimeSpan.Zero))
            {
                error = $"Job '{job.Name}' has an invalid Retry policy.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}