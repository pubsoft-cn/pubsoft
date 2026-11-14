
using System.Collections.Concurrent;

namespace PubSoft.AspNetCore;


/// <summary>
/// Bounds how many distinct client identities a single remote IP may claim within a
/// window. Client-ID headers are unauthenticated input -- without this bound a caller
/// could mint a fresh rate-limit partition per forged value. Over-cap identities are
/// refused so the request falls back to IP-only bucketing.
/// </summary>
internal sealed class ClientIdCardinalityGuard
{

    private readonly int _maxClientIdsPerIp;
    private readonly int _maxIps;
    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ConcurrentDictionary<string, IpWindow> _windows = new(StringComparer.Ordinal);

    public ClientIdCardinalityGuard(
        int maxClientIdsPerIp, int maxIps, TimeSpan window, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxClientIdsPerIp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIps);
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));
        _maxClientIdsPerIp = maxClientIdsPerIp;
        _maxIps = maxIps;
        _window = window;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Returns whether the IP may still claim this client identity this window.
    /// Already-seen identities always pass; new identities beyond the cap are refused.</summary>
    public bool Admit(string ip, string clientId)
    {
        var now = _timeProvider.GetUtcNow();
        if (!_windows.TryGetValue(ip, out var window))
        {
            // Soft bound: at capacity a racy extra entry is acceptable; the map is
            // still bounded to roughly _maxIps + concurrency.
            if (_windows.Count >= _maxIps)
                return false;
            window = _windows.GetOrAdd(ip, _ => new IpWindow { Start = now });
        }

        lock (window)
        {
            if (now - window.Start >= _window)
            {
                window.Start = now;
                window.ClientIds.Clear();
            }
            if (window.ClientIds.Contains(clientId))
                return true;
            if (window.ClientIds.Count >= _maxClientIdsPerIp)
                return false;
            window.ClientIds.Add(clientId);
            return true;
        }
    }
    private sealed class IpWindow
    {
        public DateTimeOffset Start;
        public HashSet<string> ClientIds = new(StringComparer.Ordinal);
    }
}
