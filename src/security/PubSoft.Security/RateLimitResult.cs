namespace PubSoft.Security;


/// <summary>Outcome of one rate-limit check.</summary>
public sealed class RateLimitResult
{
    /// <summary>Whether the request may proceed.</summary>
    public bool IsAllowed { get; init; }
    /// <summary>Requests left in the current window.</summary>
    public int RemainingRequests { get; init; }
    /// <summary>When the window resets.</summary>
    public DateTime ResetTime { get; init; }
    /// <summary>Why the request was rejected, if it was.</summary>
    public string? RejectionReason { get; init; }
}
