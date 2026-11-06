using System.Collections.Concurrent;

namespace PubSoft.Security;


/// <summary>A bounded, process-local fixed-window limiter with atomic per-identifier accounting.</summary>
/// <remarks>This limiter is not distributed and must not be used as a cluster-wide quota.</remarks>
public sealed class RateLimiter
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly object _capacityGate = new();
    private readonly RateLimiterOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>Creates a limiter; <paramref name="timeProvider"/> is injectable for tests.</summary>
    public RateLimiter(RateLimiterOptions? options = null, TimeProvider? timeProvider = null)
    {
        options ??= new RateLimiterOptions();
        ValidateOptions(options);
        _options = new RateLimiterOptions
        {
            RequestsPerWindow = options.RequestsPerWindow,
            WindowSize = options.WindowSize,
            Enabled = options.Enabled,
            LimitByIpAddress = options.LimitByIpAddress,
            LimitByUserId = options.LimitByUserId,
            MaxIdentifiers = options.MaxIdentifiers
        };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Counts one request against <paramref name="identifier"/> and reports the limit outcome.</summary>
    public RateLimitResult CheckRateLimit(string? identifier)
        => CheckRateLimit(identifier, countRequest: true);

    /// <summary>Checks the limit; pass <paramref name="countRequest"/> = false to probe without consuming a slot.</summary>
    public RateLimitResult CheckRateLimit(string? identifier, bool countRequest)
    {
        var now = _timeProvider.GetUtcNow();
        if (!_options.Enabled)
            return Allowed(int.MaxValue, now + _options.WindowSize);
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 256)
            return Denied("invalid_identifier", now);

        identifier = identifier.Trim();
        if (!_windows.TryGetValue(identifier, out var window))
        {
            if (!countRequest)
                return Allowed(_options.RequestsPerWindow, now + _options.WindowSize);

            lock (_capacityGate)
            {
                CleanupExpiredEntries(now);
                if (!_windows.TryGetValue(identifier, out window))
                {
                    if (_windows.Count >= _options.MaxIdentifiers)
                        return Denied("identifier_capacity_exceeded", now);

                    window = new Window(now);
                    _windows.TryAdd(identifier, window);
                }
            }
        }

        lock (window.Gate)
        {
            if (now >= window.ResetAt)
            {
                window.StartedAt = now;
                window.ResetAt = now + _options.WindowSize;
                window.Count = 0;
            }

            if (window.Count >= _options.RequestsPerWindow)
                return new RateLimitResult
                {
                    IsAllowed = false,
                    RemainingRequests = 0,
                    ResetTime = window.ResetAt.UtcDateTime,
                    RejectionReason = "rate_limit_exceeded"
                };

            if (countRequest)
                window.Count++;
            return Allowed(_options.RequestsPerWindow - window.Count, window.ResetAt);
        }
    }

    /// <summary>Checks the IP bucket when <c>LimitByIpAddress</c> is enabled; always allowed otherwise.</summary>
    public RateLimitResult CheckByIpAddress(string? ipAddress)
        => _options.LimitByIpAddress ? CheckRateLimit(ipAddress) : Allowed(int.MaxValue, _timeProvider.GetUtcNow() + _options.WindowSize);

    /// <summary>Checks the user bucket when <c>LimitByUserId</c> is enabled; always allowed otherwise.</summary>
    public RateLimitResult CheckByUserId(long userId)
        => _options.LimitByUserId ? CheckRateLimit(userId.ToString(System.Globalization.CultureInfo.InvariantCulture)) : Allowed(int.MaxValue, _timeProvider.GetUtcNow() + _options.WindowSize);

    /// <summary>Drops the bucket for <paramref name="identifier"/>, clearing its current-window count.</summary>
    public void ResetRateLimit(string? identifier)
    {
        if (!string.IsNullOrWhiteSpace(identifier))
            _windows.TryRemove(identifier.Trim(), out _);
    }

    /// <summary>Removes windows that have fully expired.</summary>
    public void CleanupExpiredEntries()
        => CleanupExpiredEntries(_timeProvider.GetUtcNow());

    /// <summary>Snapshot of tracked identifiers and request counts.</summary>
    public RateLimitStats GetStats()
    {
        var now = _timeProvider.GetUtcNow();
        var activeCount = 0;
        var totalRequests = 0;
        foreach (var window in _windows.Values)
        {
            lock (window.Gate)
            {
                if (window.ResetAt > now)
                {
                    activeCount++;
                    totalRequests += window.Count;
                }
            }
        }

        return new RateLimitStats
        {
            TotalIdentifiers = _windows.Count,
            ActiveIdentifiers = activeCount,
            TotalRequests = totalRequests
        };
    }

    private void CleanupExpiredEntries(DateTimeOffset now)
    {
        foreach (var pair in _windows)
        {
            lock (pair.Value.Gate)
            {
                if (pair.Value.ResetAt + _options.WindowSize <= now)
                    _windows.TryRemove(new KeyValuePair<string, Window>(pair.Key, pair.Value));
            }
        }
    }

    private static void ValidateOptions(RateLimiterOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RequestsPerWindow);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.WindowSize, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxIdentifiers);
    }

    private static RateLimitResult Allowed(int remaining, DateTimeOffset resetAt)
        => new() { IsAllowed = true, RemainingRequests = remaining, ResetTime = resetAt.UtcDateTime };

    private static RateLimitResult Denied(string reason, DateTimeOffset now)
        => new() { IsAllowed = false, RemainingRequests = 0, ResetTime = now.UtcDateTime, RejectionReason = reason };

    private sealed class Window(DateTimeOffset startedAt)
    {
        public object Gate { get; } = new();
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public DateTimeOffset ResetAt { get; set; } = startedAt;
        public int Count { get; set; }
    }

    /// <summary>Point-in-time limiter counters.</summary>
    public sealed class RateLimitStats
    {
        /// <summary>Identifiers ever seen.</summary>
        public int TotalIdentifiers { get; init; }
        /// <summary>Identifiers inside an active window.</summary>
        public int ActiveIdentifiers { get; init; }
        /// <summary>Requests counted across active windows.</summary>
        public int TotalRequests { get; init; }
    }
}
