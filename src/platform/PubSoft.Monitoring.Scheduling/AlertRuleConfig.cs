using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


public sealed class AlertRuleConfig
{
    public List<AlertRule> Rules { get; set; } = [];
}
