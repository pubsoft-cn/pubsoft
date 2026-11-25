namespace PubSoft.Monitoring.Scheduling;


/// <summary>Tuning for the alert-rule processor.</summary>
public sealed class AlertProcessorOptions
{
    /// <summary>Consecutive failures before the confirmed state fires.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>Egress-outage alerting; disabled by default.</summary>
    public EnvironmentAlertOptions Environment { get; set; } = new();

    /// <summary>Undelivered-alert resend; disabled by default.</summary>
    public PendingAlertOptions PendingAlerts { get; set; } = new();

    internal bool IsValid() => IsValid(out _);

    internal bool IsValid(out string? error)
    {
        error = null;
        if (FailureThreshold <= 0)
        { error = "FailureThreshold must be positive."; return false; }
        if (Environment is null)
        { error = "Environment must not be null."; return false; }
        if (!Environment.IsValid(out error))
            return false;
        if (PendingAlerts is null)
        { error = "PendingAlerts must not be null."; return false; }
        return PendingAlerts.IsValid(out error);
    }
}
