namespace PubSoft.Monitoring.Scheduling;


/// <summary>Alert severity for scheduled-job rules.</summary>
public enum AlertSeverity
{
    /// <summary>Informational notice.</summary>
    Info,
    /// <summary>Operator attention advised.</summary>
    Warning,
    /// <summary>Immediate action expected.</summary>
    Critical
}
