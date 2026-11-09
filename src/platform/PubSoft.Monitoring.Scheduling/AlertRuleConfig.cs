using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


/// <summary>Configuration section carrying the declared alert rules.</summary>
public sealed class AlertRuleConfig
{
    /// <summary>Declared alert rules.</summary>
    public List<AlertRule> Rules { get; set; } = [];
}
