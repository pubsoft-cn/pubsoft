namespace PubSoft.Authentication.Jwt;

/// <summary>Options for <see cref="JwksClientAssertionKeyProvider"/>.</summary>
public sealed class JwksClientAssertionKeyOptions
{
    /// <summary>
    /// Fetches one client's JWKS document (RFC 7517 JSON text) from the credential registry,
    /// keyed by client id. Return null when the registry is unavailable or the client is unknown.
    /// Required. The registry call typically authenticates the caller itself - supply that
    /// credential inside the delegate; PubSoft does not bind to a registry SDK.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? FetchJwks { get; set; }

    /// <summary>
    /// How long a fetched key set is served before re-fetching (default 5 minutes). Rotated-out
    /// keys stop verifying within this window - the provider never serves expired sets.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a failed or empty fetch is negatively cached (default 1 minute) - bounds the
    /// registry call rate when unknown clients are probed.
    /// </summary>
    public TimeSpan MissCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(FetchJwks);
        if (CacheDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CacheDuration), "CacheDuration must be positive.");
        if (MissCacheDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MissCacheDuration), "MissCacheDuration must be positive.");
    }
}
