namespace PubSoft.Edge;

/// <summary>
/// Options for <see cref="RemoteEdgeSignKeyProvider"/>.
/// </summary>
public sealed class RemoteEdgeSignKeyOptions
{
    /// <summary>
    /// Fetches this service's registered edge sign keys from the credential registry.
    /// Return null when the registry is unavailable - the provider keeps the last good pool.
    /// Required at registration.
    /// </summary>
    /// <remarks>
    /// The registry call typically authenticates the caller itself (self-scoped: a service may
    /// only read keys registered under its own identity) - supply the caller credential inside
    /// the delegate; PubSoft does not bind to a specific registry SDK.
    /// </remarks>
    public Func<CancellationToken, Task<IReadOnlyList<EdgeSignKeyEntry>?>>? FetchKeys { get; set; }

    /// <summary>
    /// Local fallback sign keys (clientId -> secretKey) - dev/emergency hotfix source seeded
    /// before the first fetch and re-merged on every refresh. Registry entries win on conflict.
    /// </summary>
    public IDictionary<string, string>? LocalKeys { get; set; }

    /// <summary>
    /// Registry refresh interval (default 5 minutes). Key additions/rotations propagate to
    /// verifiers within one interval - a rotated key keeps verifying until the next refresh.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Pull synchronously on the first key lookup miss until the registry has been read once
    /// (default true). Prevents cold-start rejections while the background refresh is pending.
    /// </summary>
    public bool RefreshOnMiss { get; set; } = true;

    /// <summary>
    /// Upper bound for the synchronous first-miss pull, including the wait for an in-flight
    /// refresh (default 10 seconds). On timeout the lookup fails closed.
    /// </summary>
    public TimeSpan RefreshOnMissTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(FetchKeys);
        if (RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshInterval), "RefreshInterval must be positive.");
        if (RefreshOnMissTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshOnMissTimeout), "RefreshOnMissTimeout must be positive.");
    }
}
