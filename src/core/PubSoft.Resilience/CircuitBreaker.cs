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
            state.FailureCount = 0;
            state.HalfOpenProbeInFlight = false;
            if (state.Status == State.HalfOpen)
            {
                state.Status = State.Closed;
                _logger?.LogInformation("Circuit breaker for {Host} transitioned to Closed", host);
            }
        }
    }

    /// <summary>Marks a call as failed; opens the circuit at the threshold.</summary>
    public void RecordFailure(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var state = _states.GetOrAdd(host, static _ => new CircuitState());
        lock (state.Sync)
        {
            state.FailureCount++;
            state.HalfOpenProbeInFlight = false;
            if (state.Status == State.HalfOpen || state.FailureCount >= Options.FailureThreshold)
            {
                state.Status = State.Open;
                state.OpenUntil = _timeProvider.GetUtcNow().Add(Options.OpenTimeout);
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

    private sealed class CircuitState
    {
        public object Sync { get; } = new();
        public State Status { get; set; }
        public int FailureCount { get; set; }
        public DateTimeOffset OpenUntil { get; set; }
        public bool HalfOpenProbeInFlight { get; set; }
    }
}

/// <summary>Circuit-breaker thresholds.</summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>Consecutive failures that open the circuit.</summary>
    public int FailureThreshold { get; set; } = 5;
    /// <summary>How long the circuit stays open before allowing a probe.</summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
