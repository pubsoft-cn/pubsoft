namespace PubSoft.Monitoring.Scheduling;

/// <summary>Derived health state of a scheduled job.</summary>
public enum JobHealthState
{
    /// <summary>Running within expected bounds.</summary>
    Healthy,
    /// <summary>Anomalies observed below the failure threshold.</summary>
    Suspicious,
    /// <summary>Failure threshold crossed.</summary>
    Unhealthy
}
