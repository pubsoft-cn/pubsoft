using System.Collections.Concurrent;

namespace PubSoft.Security;


public sealed class RateLimitResult
{
    public bool IsAllowed { get; init; }
    public int RemainingRequests { get; init; }
    public DateTime ResetTime { get; init; }
    public string? RejectionReason { get; init; }
}
