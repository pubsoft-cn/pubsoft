namespace PubSoft.Scheduling;

public sealed class SchedulerOptions
{
    public List<ScheduledJob> Jobs { get; set; } = [];
    public TimeSpan LoopInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxConcurrentJobs { get; set; } = 16;

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