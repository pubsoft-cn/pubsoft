namespace PubSoft.Monitoring.Scheduling;


/// <summary>A declared alert rule: which condition fires, at what severity, through which channels.</summary>
public sealed class AlertRule
{
    /// <summary>Rule identifier (used for throttling keys).</summary>
    public string Name { get; set; } = "";
    /// <summary>Condition that fires the alert.</summary>
    public AlertConditionType Condition { get; set; }
    /// <summary>Alert severity.</summary>
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    /// <summary>Notification channels to dispatch through.</summary>
    public string[] Channels { get; set; } = [];
    /// <summary>Optional recipients forwarded to the dispatched notification (e.g. SMS numbers); null means the channel decides.</summary>
    public string[]? Recipients { get; set; }
    /// <summary>Optional channel template code forwarded to the dispatched notification.</summary>
    public string? TemplateCode { get; set; }
    /// <summary>Optional static template parameters forwarded to the dispatched notification. Entries override the built-in parameters (<c>job</c>, <c>status</c>, <c>state</c>, <c>severity</c>) on key collision.</summary>
    public Dictionary<string, string>? TemplateParameters { get; set; }
    /// <summary>Minimum interval between repeats of the same rule firing.</summary>
    public TimeSpan? Throttle { get; set; }
    /// <summary>Duration bound in ms for the slow-response condition.</summary>
    public int? DelayThresholdMs { get; set; }
}
