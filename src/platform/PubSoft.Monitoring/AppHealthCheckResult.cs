namespace PubSoft.Monitoring;


public readonly record struct AppHealthCheckResult(AppHealthStatus Status, string? Description = null)
{
    public static AppHealthCheckResult Healthy(string? description = null)
        => new(AppHealthStatus.Healthy, description);

    public static AppHealthCheckResult Degraded(string description)
        => new(AppHealthStatus.Degraded, description);

    public static AppHealthCheckResult Unhealthy(string description)
        => new(AppHealthStatus.Unhealthy, description);
}
