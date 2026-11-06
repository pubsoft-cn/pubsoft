namespace PubSoft.Monitoring;


public interface IAppHealthCheck
{
    string Name { get; }
    ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
