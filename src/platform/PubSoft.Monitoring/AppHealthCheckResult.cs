namespace PubSoft.Monitoring;


/// <summary>One health-check outcome.</summary>
/// <param name="Status">Health verdict.</param>
/// <param name="Description">Optional detail.</param>
public readonly record struct AppHealthCheckResult(AppHealthStatus Status, string? Description = null)
{
    /// <summary>Healthy result.</summary>
    public static AppHealthCheckResult Healthy(string? description = null)
        => new(AppHealthStatus.Healthy, description);

    /// <summary>Degraded result.</summary>
    public static AppHealthCheckResult Degraded(string description)
        => new(AppHealthStatus.Degraded, description);

    /// <summary>Unhealthy result.</summary>
    public static AppHealthCheckResult Unhealthy(string description)
        => new(AppHealthStatus.Unhealthy, description);
}
