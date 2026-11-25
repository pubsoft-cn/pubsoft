namespace PubSoft.Monitoring.Scheduling;

/// <summary>Undelivered-alert resend; disabled by default.</summary>
public sealed class PendingAlertOptions
{
    /// <summary>Persist alerts that no channel could deliver and retry them on later job executions until delivered or expired.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long an undelivered alert stays eligible for resend.</summary>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Maximum pending entries kept per job; the oldest are dropped first.</summary>
    public int MaxEntries { get; set; } = 50;

    internal bool IsValid(out string? error)
    {
        error = null;
        if (Expiry <= TimeSpan.Zero)
        {
            error = "PendingAlerts.Expiry must be positive.";
            return false;
        }
        if (MaxEntries <= 0)
        {
            error = "PendingAlerts.MaxEntries must be positive.";
            return false;
        }
        return true;
    }
}
