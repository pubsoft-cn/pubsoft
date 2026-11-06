namespace PubSoft.Monitoring;

public enum AppHealthStatus : byte
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}
