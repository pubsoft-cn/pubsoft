namespace PubSoft.Monitoring;


/// <summary>Checks whether the configured probe targets are reachable.</summary>
public interface IProbeHealthCheck
{
    Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default);
}
