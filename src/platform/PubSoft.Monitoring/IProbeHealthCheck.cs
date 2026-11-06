namespace PubSoft.Monitoring;


/// <summary>Checks whether the configured probe targets are reachable.</summary>
public interface IProbeHealthCheck
{
    /// <summary>Runs the probe.</summary>
    Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default);
}
