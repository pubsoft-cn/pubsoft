namespace PubSoft.Monitoring;

/// <summary>Aggregate reachability state for the configured probe targets.</summary>
public enum ProbeHealth
{
    Healthy,
    Unreachable,
    Degraded,
}

/// <summary>Checks whether the configured probe targets are reachable.</summary>
public interface IProbeHealthCheck
{
    Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default);
}