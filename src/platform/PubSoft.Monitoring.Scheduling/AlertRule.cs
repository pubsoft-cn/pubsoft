using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


public sealed class AlertRule
{
    public string Name { get; set; } = "";
    public AlertConditionType Condition { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public string[] Channels { get; set; } = [];
    public TimeSpan? Throttle { get; set; }
    public int? DelayThresholdMs { get; set; }
}
