namespace PubSoft.Monitoring;

/// <summary>Aggregate reachability state for the configured probe targets.</summary>
public enum ProbeHealth
{
    Healthy,
    Unreachable,
    Degraded,
}
