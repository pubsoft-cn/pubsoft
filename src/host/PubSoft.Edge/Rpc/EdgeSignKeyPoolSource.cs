namespace PubSoft.Edge;

/// <summary>Which key material the remote edge sign-key pool is currently serving.</summary>
public enum EdgeSignKeyPoolSource
{
    /// <summary>No remote bundle has been applied; serving the configured local fallback keys.</summary>
    LocalFallback,

    /// <summary>Remote bundle loaded and the last fetch succeeded.</summary>
    Remote,

    /// <summary>A remote bundle was loaded earlier but the last fetch failed or returned nothing; serving the last known good pool.</summary>
    Degraded
}
