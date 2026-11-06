namespace PubSoft.Monitoring;


/// <summary>One named health probe.</summary>
public interface IAppHealthCheck
{
    /// <summary>Probe name.</summary>
    string Name { get; }
    /// <summary>Runs the probe.</summary>
    ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
