namespace PubSoft.Scheduling;

/// <summary>Runtime identity of one job execution, distinct from the static job configuration.</summary>
/// <param name="ScheduledTime">The calendar occurrence being executed: the current tick for exact runs, or the recovered occurrence for misfire recovery.</param>
/// <param name="IsMisfireRecovery">Whether this execution is a compressed catch-up run for a missed occurrence.</param>
/// <param name="FencingToken">
/// Monotonic token assigned by a fencing-capable distributed lock; null when distributed locking is off
/// or the implementation does not fence. The lease remains only a time bound - protected resources must
/// validate this token to reject a stale holder.
/// </param>
public sealed record JobExecutionContext(
    DateTimeOffset ScheduledTime,
    bool IsMisfireRecovery,
    long? FencingToken);
