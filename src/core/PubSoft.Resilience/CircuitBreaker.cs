using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Resilience;

/// <summary>Per-host circuit breaker: opens after a failure streak, half-opens for a probe after the cool-down.</summary>
public sealed class CircuitBreaker
{

    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CircuitBreaker>? _logger;
    private readonly TimeProvider _timeProvider = TimeProvider.System;


    /// <summary>Creates a breaker; <paramref name="timeProvider"/> is injectable for tests.</summary>
    public CircuitBreaker(ILogger<CircuitBreaker>? logger = null, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Trip thresholds and cool-down.</summary>
    public CircuitBreakerOptions Options { get; set; } = new();

    /// <summary>Whether a call to <paramref name="host"/> may proceed - false while open (except one half-open probe).</summary>
    public bool TryAcquire(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var state = _states.GetOrAdd(host, static _ => new CircuitState());
        lock (state.Sync)
        {
            if (state.Status == State.Closed) return true;
            if (state.Status == State.Open)
            {
                if (_timeProvider.GetUtcNow() < state.OpenUntil) return false;
                state.Status = State.HalfOpen;
                state.HalfOpenProbeInFlight = true;
                _logger?.LogInformation("Circuit breaker for {Host} transitioned to HalfOpen", host);
                return true;
            }

            if (state.HalfOpenProbeInFlight) return false;
            state.HalfOpenProbeInFlight = true;
            return true;
        }
    }

    /// <summary>Marks a call as successful, resetting the streak and closing a half-open circuit.</summary>
    public void RecordSuccess(string host)
    {
        if (!_states.TryGetValue(host, out var state)) return;
        lock (state.Sync)
        {
            var now = _timeProvider.GetUtcNow();
            RecordRollingSample(state, now, failure: false);
            state.FailureCount = 0;
            state.HalfOpenProbeInFlight = false;
            if (state.Status == State.HalfOpen)
            {
                state.Status = State.Closed;
                _logger?.LogInformation("Circuit breaker for {Host} transitioned to Closed", host);
            }
        }
    }

    /// <summary>Marks a call as failed; opens the circuit at the configured threshold.</summary>
    public void RecordFailure(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var state = _states.GetOrAdd(host, static _ => new CircuitState());
        lock (state.Sync)
        {
            var now = _timeProvider.GetUtcNow();
            RecordRollingSample(state, now, failure: true);
            state.FailureCount++;
            state.HalfOpenProbeInFlight = false;
            if (state.Status == State.HalfOpen || ShouldOpen(state, now))
            {
                state.Status = State.Open;
                state.OpenUntil = now.Add(Options.OpenTimeout);
                _logger?.LogWarning("Circuit breaker for {Host} opened after {Count} failures until {Until}",
                    host, state.FailureCount, state.OpenUntil);
            }
        }
    }

    /// <summary>Drops all state for <paramref name="host"/> (e.g. after a config reload).</summary>
    public void Abandon(string host)
    {
        if (!_states.TryGetValue(host, out var state)) return;
        lock (state.Sync)
        {
            if (state.Status == State.HalfOpen)
                state.HalfOpenProbeInFlight = false;
        }
    }

    /// <summary>Current circuit state for a host.</summary>
    public State GetState(string host)
    {
        if (!_states.TryGetValue(host, out var state)) return State.Closed;
        lock (state.Sync) return state.Status;
    }
    private bool ShouldOpen(CircuitState state, DateTimeOffset now)
    {
        if (Options.Mode == CircuitBreakerMode.ConsecutiveFailures)
            return state.FailureCount >= Options.FailureThreshold;
        if (Options.FailureRatio <= 0 || Options.FailureRatio > 1
            || Options.MinimumThroughput <= 0 || Options.SamplingDuration <= TimeSpan.Zero)
            return false;

        var bucketTicks = RollingBucketTicks(Options.SamplingDuration);
        var currentBucket = now.UtcTicks / bucketTicks;
        var oldestBucket = currentBucket - CircuitState.RollingBucketCount + 1;
        var total = 0;
        var failures = 0;
        foreach (var bucket in state.RollingBuckets)
        {
            if (bucket.Index < oldestBucket || bucket.Index > currentBucket)
                continue;
            total += bucket.Total;
            failures += bucket.Failures;
        }
        return total >= Options.MinimumThroughput && failures / (double)total >= Options.FailureRatio;
    }

    private void RecordRollingSample(CircuitState state, DateTimeOffset now, bool failure)
    {
        if (Options.Mode != CircuitBreakerMode.RollingFailureRatio || Options.SamplingDuration <= TimeSpan.Zero)
            return;
        var bucketTicks = RollingBucketTicks(Options.SamplingDuration);
        var bucketIndex = now.UtcTicks / bucketTicks;
        var slot = (int)(((bucketIndex % CircuitState.RollingBucketCount) + CircuitState.RollingBucketCount) % CircuitState.RollingBucketCount);
        if (state.RollingBuckets[slot].Index != bucketIndex)
            state.RollingBuckets[slot] = new RollingBucket { Index = bucketIndex };
        state.RollingBuckets[slot].Total++;
        if (failure)
            state.RollingBuckets[slot].Failures++;
    }

    private static long RollingBucketTicks(TimeSpan samplingDuration)
        => Math.Max(1, samplingDuration.Ticks / CircuitState.RollingBucketCount);

    /// <summary>Circuit state.</summary>
    public enum State
    {
        /// <summary>Calls flow normally.</summary>
        Closed,
        /// <summary>Calls are rejected fast.</summary>
        Open,
        /// <summary>One probe call is allowed.</summary>
        HalfOpen
    }

    private struct RollingBucket
    {
        public long Index { get; set; }
        public int Total { get; set; }
        public int Failures { get; set; }
    }

    private sealed class CircuitState
    {
        public const int RollingBucketCount = 30;
        public object Sync { get; } = new();
        public State Status { get; set; }
        public int FailureCount { get; set; }
        public DateTimeOffset OpenUntil { get; set; }
        public bool HalfOpenProbeInFlight { get; set; }
        public RollingBucket[] RollingBuckets { get; } = new RollingBucket[RollingBucketCount];
    }
}

/// <summary>Circuit-breaker thresholds.</summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>Failure measurement strategy.</summary>
    public CircuitBreakerMode Mode { get; set; } = CircuitBreakerMode.ConsecutiveFailures;
    /// <summary>Consecutive failures that open the circuit in <see cref="CircuitBreakerMode.ConsecutiveFailures"/> mode.</summary>
    public int FailureThreshold { get; set; } = 5;
    /// <summary>Failure ratio that opens the circuit in rolling-ratio mode; must be in (0, 1].</summary>
    public double FailureRatio { get; set; } = 0.5;
    /// <summary>Minimum rolling-window calls required before the ratio can open the circuit.</summary>
    public int MinimumThroughput { get; set; } = 10;
    /// <summary>Rolling observation window for ratio mode.</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>How long the circuit stays open before allowing a probe.</summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Circuit-breaker failure measurement strategy.</summary>
public enum CircuitBreakerMode
{
    /// <summary>Open after a consecutive failure streak.</summary>
    ConsecutiveFailures,
    /// <summary>Open when failures reach a ratio over a rolling time window and minimum throughput.</summary>
    RollingFailureRatio
}
