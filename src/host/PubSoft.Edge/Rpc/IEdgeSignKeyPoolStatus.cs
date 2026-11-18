namespace PubSoft.Edge;

/// <summary>
/// Read-only view of the remote edge sign-key pool's freshness. Resolve in
/// readiness checks to make a silent remote-fetch degradation visible to ops -
/// the provider keeps serving the last good pool by design, so a persistent
/// outage is otherwise only a warning log line.
/// </summary>
public interface IEdgeSignKeyPoolStatus
{
    /// <summary>Which key material is currently being served.</summary>
    EdgeSignKeyPoolSource Source { get; }

    /// <summary>UTC time of the last successful remote fetch, or null when none has succeeded.</summary>
    DateTimeOffset? LastFetchSucceededUtc { get; }

    /// <summary>Why the last fetch did not apply, or null when the last fetch succeeded.</summary>
    string? LastFetchError { get; }
}
