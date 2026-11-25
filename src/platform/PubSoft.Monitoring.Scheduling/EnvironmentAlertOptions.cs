namespace PubSoft.Monitoring.Scheduling;

/// <summary>
/// Opt-in alerting for monitoring-egress outages: while probe targets are unreachable, job alerts are
/// suppressed and the outage itself is silent. When enabled, a summary is dispatched through the
/// configured channels once the probe recovers -- the alert necessarily fires at recovery time, not at
/// outage start, because the same egress is down.
/// </summary>
public sealed class EnvironmentAlertOptions
{
    /// <summary>Enables egress-outage alerting. Fail-closed: enabling requires at least one channel.</summary>
    public bool Enabled { get; set; }
    /// <summary>Minimum continuous outage before the recovery summary is dispatched; shorter blips are logged but not alerted.</summary>
    public TimeSpan MinUnreachableDuration { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>Channels the environment alert dispatches through.</summary>
    public string[] Channels { get; set; } = [];
    /// <summary>Severity of the recovery summary.</summary>
    public AlertSeverity Severity { get; set; } = AlertSeverity.Critical;
    /// <summary>
    /// Also attempt one immediate notification when the outage is first observed. It usually fails while
    /// egress is down, but lands early in partial-connectivity cases; at most one attempt per outage window.
    /// </summary>
    public bool SendImmediate { get; set; }

    internal bool IsValid(out string? error)
    {
        error = null;
        if (!Enabled)
            return true;
        if (Channels is not { Length: >= 1 } || Channels.Any(string.IsNullOrWhiteSpace))
        { error = "Environment.Channels must name at least one non-empty channel when Enabled."; return false; }
        if (MinUnreachableDuration <= TimeSpan.Zero)
        { error = "Environment.MinUnreachableDuration must be positive."; return false; }
        if (!Enum.IsDefined(Severity))
        { error = "Environment.Severity is not a declared value."; return false; }
        return true;
    }
}
