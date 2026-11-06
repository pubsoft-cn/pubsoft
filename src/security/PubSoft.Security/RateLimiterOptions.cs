using System.Collections.Concurrent;

namespace PubSoft.Security;

/// <summary>Configures the sliding-window rate limiter.</summary>
public sealed class RateLimiterOptions
{
    /// <summary>Requests allowed per identifier per window.</summary>
    public int RequestsPerWindow { get; set; } = 100;
    /// <summary>Length of the sliding window.</summary>
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Whether limiting is active.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Whether to limit by caller IP.</summary>
    public bool LimitByIpAddress { get; set; } = true;
    /// <summary>Whether to limit by authenticated user id.</summary>
    public bool LimitByUserId { get; set; }
    /// <summary>Cap on tracked identifier buckets - bounds memory.</summary>
    public int MaxIdentifiers { get; set; } = 100_000;
}
