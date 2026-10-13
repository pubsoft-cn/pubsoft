using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Resilience;

public sealed class CircuitBreaker
{
    public enum State { Closed, Open, HalfOpen }

    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CircuitBreaker>? _logger;

    public CircuitBreaker(ILogger<CircuitBreaker>? logger = null) => _logger = logger;

    public CircuitBreakerOptions Options { get; set; } = new();

    public bool TryAcquire(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var state = _states.GetOrAdd(host, static _ => new CircuitState());
        lock (state.Sync)
        {
            if (state.Status == State.Closed) return true;
            if (state.Status == State.Open)
            {
                if (DateTimeOffset.UtcNow < state.OpenUntil) return false;
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
                state.OpenUntil = DateTimeOffset.UtcNow.Add(Options.OpenTimeout);
                _logger?.LogWarning("Circuit breaker for {Host} opened after {Count} failures until {Until}",
                    host, state.FailureCount, state.OpenUntil);
            }
        }
    }

    public void Abandon(string host)
    {
        if (!_states.TryGetValue(host, out var state)) return;
        lock (state.Sync)
        {
            if (state.Status == State.HalfOpen)
                state.HalfOpenProbeInFlight = false;
        }
    }

    public State GetState(string host)
    {
        if (!_states.TryGetValue(host, out var state)) return State.Closed;
        lock (state.Sync) return state.Status;
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

public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
