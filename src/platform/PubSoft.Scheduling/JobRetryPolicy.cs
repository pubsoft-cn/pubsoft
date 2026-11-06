using System.Text.Json;

namespace PubSoft.Scheduling;


/// <summary>Bounded exponential-backoff retry for a <see cref="ScheduledJob"/>.</summary>
[PubSoft.Core.BoundedCost("data object — timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public sealed class JobRetryPolicy
{
    /// <summary>Total attempts including the first execution (1 = no retry).</summary>
    public int MaxAttempts { get; set; } = 1;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    public double BackoffFactor { get; set; } = 2.0;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
}
