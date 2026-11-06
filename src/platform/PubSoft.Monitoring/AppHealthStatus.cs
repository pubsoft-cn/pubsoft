namespace PubSoft.Monitoring;

/// <summary>Overall health verdict.</summary>
public enum AppHealthStatus : byte
{
    /// <summary>All good.</summary>
    Healthy = 0,
    /// <summary>Working but impaired.</summary>
    Degraded = 1,
    /// <summary>Failing.</summary>
    Unhealthy = 2
}
