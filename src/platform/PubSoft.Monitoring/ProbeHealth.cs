namespace PubSoft.Monitoring;

/// <summary>Aggregate reachability state for the configured probe targets.</summary>
public enum ProbeHealth
{
    /// <summary>All probe targets reachable.</summary>
    Healthy,
    /// <summary>No probe target reachable.</summary>
    Unreachable,
    /// <summary>Some targets reachable.</summary>
    Degraded,
}
