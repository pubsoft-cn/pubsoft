using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;

public enum JobHealthState
{
    Healthy,
    Suspicious,
    Unhealthy
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertConditionType
{
    StateDegraded,
    StateConfirmed,
    StateRecovered,
    SlowResponse,
    FirstFailure,
    Recovered,
    EveryFailure
}

public sealed class AlertRule
{
    public string Name { get; set; } = "";
    public AlertConditionType Condition { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public string[] Channels { get; set; } = [];
    public TimeSpan? Throttle { get; set; }
    public int? DelayThresholdMs { get; set; }
}

public sealed class AlertRuleConfig
{
    public List<AlertRule> Rules { get; set; } = [];
}

public sealed class AlertProcessorOptions
{
    public int FailureThreshold { get; set; } = 3;

    internal bool IsValid() => FailureThreshold > 0;
}

public sealed class AlertContext
{
    public required ScheduledJob Job { get; init; }
    public required JobResult Current { get; init; }
    public JobHealthState FromState { get; init; }
    public JobHealthState ToState { get; init; }
    public ProbeHealth ProbeHealth { get; init; }
    public bool WasProbeUnreachable { get; init; }
}