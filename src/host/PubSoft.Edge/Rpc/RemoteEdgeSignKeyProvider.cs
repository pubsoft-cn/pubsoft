using Microsoft.Extensions.Logging;

namespace PubSoft.Edge;

/// <summary>
/// <see cref="IEdgeSignKeyProvider"/> + <see cref="IEdgeSchemePolicy"/> that periodically pulls
/// this service's edge sign-key bundle from a credential registry and merges it over optional
/// local fallback keys (registry wins on conflict).
/// </summary>
/// <remarks>
/// Refresh semantics: initial fetch starts at construction and repeats every
/// <see cref="RemoteEdgeSignKeyOptions.RefreshInterval"/>; a failed fetch keeps the last good
/// pool. Until the first successful fetch, a key miss triggers a synchronous pull when
/// <see cref="RemoteEdgeSignKeyOptions.RefreshOnMiss"/> is enabled. Unknown clientIds return
/// null - the gateway then fails closed.
/// </remarks>
public sealed class RemoteEdgeSignKeyProvider : IEdgeSignKeyProvider, IEdgeSchemePolicy, IDisposable
{
    private readonly RemoteEdgeSignKeyOptions _options;
    private readonly ILogger<RemoteEdgeSignKeyProvider>? _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;

    private volatile Dictionary<string, (string Key, string? Scheme)> _keys;
    private volatile bool _remoteLoaded;

    /// <summary>Creates the provider, seeds local fallback keys, and starts the refresh loop.</summary>
    public RemoteEdgeSignKeyProvider(RemoteEdgeSignKeyOptions options, ILogger<RemoteEdgeSignKeyProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _logger = logger;

        _keys = LocalKeyMap();
        _ = RefreshAsync();
        _refreshTimer = new Timer(_ => _ = RefreshAsync(), null, _options.RefreshInterval, _options.RefreshInterval);
    }

    /// <inheritdoc />
    public bool ShowSignDebug => false;

    /// <inheritdoc />
    public string? GetSecretKey(string clientId)
    {
        if (!_keys.TryGetValue(clientId, out var entry))
        {
            if (!_remoteLoaded && _options.RefreshOnMiss)
                RefreshAsync(waitForLock: true).GetAwaiter().GetResult(); // pubsoft:exempt PUBSOFT017 "synchronous SPI; first-miss pull is bounded by RefreshOnMissTimeout"
            _keys.TryGetValue(clientId, out entry);
        }
        return entry.Key;
    }

    /// <inheritdoc />
    public EdgeAuthScheme? GetAllowedScheme(string clientId)
    {
        if (!_keys.TryGetValue(clientId, out var entry) || entry.Scheme is null)
            return null;
        return entry.Scheme.Trim().ToLowerInvariant() switch
        {
            "hmac" => EdgeAuthScheme.Hmac,
            "md5" => EdgeAuthScheme.Md5,
            "session" or "sessionkey" => EdgeAuthScheme.Session,
            "none" => EdgeAuthScheme.None,
            _ => null,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
    }

    private async Task RefreshAsync(bool waitForLock = false)
    {
        // The synchronous first-miss pull waits for an in-flight refresh (bounded); the
        // background timer skips instead of stacking behind a slow fetch.
        var acquired = waitForLock
            ? await _refreshLock.WaitAsync(_options.RefreshOnMissTimeout).ConfigureAwait(false)
            : await _refreshLock.WaitAsync(0).ConfigureAwait(false);
        if (!acquired)
            return;
        try
        {
            var entries = await _options.FetchKeys!(CancellationToken.None).ConfigureAwait(false);
            if (entries is null)
                return;

            var merged = LocalKeyMap();
            foreach (var entry in entries)
                if (!string.IsNullOrWhiteSpace(entry.ClientId) && !string.IsNullOrWhiteSpace(entry.SecretKey))
                    merged[entry.ClientId] = (entry.SecretKey, entry.Scheme);

            _keys = merged;
            _remoteLoaded = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to pull the edge sign-key bundle - keeping the last good pool");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private Dictionary<string, (string Key, string? Scheme)> LocalKeyMap()
    {
        var map = new Dictionary<string, (string Key, string? Scheme)>(StringComparer.Ordinal);
        if (_options.LocalKeys is not null)
            foreach (var kv in _options.LocalKeys)
                if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    map[kv.Key] = (kv.Value, null);
        return map;
    }

}
