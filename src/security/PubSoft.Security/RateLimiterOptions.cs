using System.Collections.Concurrent;

namespace PubSoft.Security;

public sealed class RateLimiterOptions
{
    public int RequestsPerWindow { get; set; } = 100;
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
    public bool Enabled { get; set; } = true;
    public bool LimitByIpAddress { get; set; } = true;
    public bool LimitByUserId { get; set; }
    public int MaxIdentifiers { get; set; } = 100_000;
}
