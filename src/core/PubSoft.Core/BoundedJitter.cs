namespace PubSoft.Core;

/// <summary>
/// Bounded symmetric jitter for retry and reconnect delays. The sampled delay stays inside the
/// (1 - ratio) to (1 + ratio) band around the base delay, so a fleet of replicas facing the same
/// outage does not retry in lockstep while the delay never collapses to zero or explodes.
/// </summary>
public static class BoundedJitter
{
    /// <summary>
    /// Samples uniformly in the symmetric band around <paramref name="baseDelay"/>.
    /// A ratio of 0 returns the base delay unchanged; 0.2 means the 80%-120% band.
    /// </summary>
    public static TimeSpan Apply(TimeSpan baseDelay, double jitterRatio)
    {
        if (!double.IsFinite(jitterRatio) || jitterRatio < 0 || jitterRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(jitterRatio), "Jitter ratio must be a finite value between 0 and 1.");
        if (baseDelay <= TimeSpan.Zero || jitterRatio == 0)
            return baseDelay;

        var factor = 1 - jitterRatio + (Random.Shared.NextDouble() * 2 * jitterRatio);
        var jitteredTicks = (long)Math.Round(baseDelay.Ticks * factor, MidpointRounding.AwayFromZero);
        return TimeSpan.FromTicks(Math.Max(jitteredTicks, 0));
    }
}
