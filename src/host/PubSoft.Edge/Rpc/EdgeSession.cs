using PubSoft.Core;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Edge;

/// <summary>
/// Edge session — user data associated with a sessionKey.
/// </summary>
public sealed record EdgeSession
{
    /// <summary>sessionKey (format: clientId:appId:guid).</summary>
    public string SessionKey { get; init; } = string.Empty;

    /// <summary>Backend token bound to the session (never exposed to the caller).</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>User id.</summary>
    public string? UserId { get; init; }

    /// <summary>Member id (C-end business use).</summary>
    public string? MemberId { get; init; }

    /// <summary>OpenId (provider user id).</summary>
    public string? OpenId { get; init; }

    /// <summary>Roles.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>Tenant id.</summary>
    public string? TenantId { get; init; }

    /// <summary>Expiry (Unix epoch ms).</summary>
    public long ExpiresAt { get; init; }

    /// <summary>Whether the session is expired.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > ExpiresAt;
}

/// <summary>
/// sessionKey store — in-memory default; replaceable by a distributed implementation.
/// </summary>
public interface IEdgeSessionStore
{
    /// <summary>Saves a session.</summary>
    Task SaveAsync(EdgeSession session, CancellationToken ct = default);

    /// <summary>Gets a session by sessionKey.</summary>
    Task<EdgeSession?> GetAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>Deletes a session (active revocation).</summary>
    Task RemoveAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>Renews a session (sliding expiry).</summary>
    Task RenewAsync(string sessionKey, TimeSpan extendBy, CancellationToken ct = default);
}

/// <summary>
/// In-memory sessionKey store — default, for single-instance deployments.
/// Multi-instance deployments should plug in a distributed store.
/// </summary>
public sealed class InMemoryEdgeSessionStore : IEdgeSessionStore, IDisposable
{
    private readonly ConcurrentDictionary<string, EdgeSession> _sessions = new();
    private readonly Timer _cleanupTimer;

    public InMemoryEdgeSessionStore()
    {
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task SaveAsync(EdgeSession session, CancellationToken ct = default)
    {
        _sessions[session.SessionKey] = session;
        return Task.CompletedTask;
    }

    public Task<EdgeSession?> GetAsync(string sessionKey, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionKey, out var session) && session.IsExpired)
        {
            _sessions.TryRemove(sessionKey, out _);
            session = null;
        }
        return Task.FromResult(session);
    }

    public Task RemoveAsync(string sessionKey, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionKey, out _);
        return Task.CompletedTask;
    }

    public Task RenewAsync(string sessionKey, TimeSpan extendBy, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionKey, out var session) && !session.IsExpired)
        {
            _sessions[sessionKey] = session with
            {
                ExpiresAt = DateTimeOffset.UtcNow.Add(extendBy).ToUnixTimeMilliseconds()
            };
        }
        return Task.CompletedTask;
    }

    [PubSoft.Core.BoundedCost("timer purge — bounded periodic scan")]
    private void CleanupExpired(object? state)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var kv in _sessions.Where(kv => kv.Value.ExpiresAt < now).ToList())
            _sessions.TryRemove(kv.Key, out _);
    }

    public void Dispose() => _cleanupTimer.Dispose();
}

/// <summary>
/// Edge session manager — sessionKey issue, validate, revoke.
///
/// sessionKey format: {clientId}:{appId}:{guid}
/// - Far shorter than a backend token (~50 chars vs several hundred)
/// - Server-side revocable
/// - Backend token never reaches the caller
/// </summary>
public sealed class EdgeSessionManager
{
    private readonly IEdgeSessionStore _store;
    private readonly TimeSpan _defaultExpiry;

    public EdgeSessionManager(IEdgeSessionStore store, TimeSpan? defaultExpiry = null)
    {
        _store = store;
        _defaultExpiry = defaultExpiry ?? TimeSpan.FromHours(2);
    }

    /// <summary>Creates a session after login; returns the sessionKey for the caller.</summary>
    public async Task<string> CreateAsync(
        string clientId,
        string appId,
        string token,
        string? userId = null,
        string? memberId = null,
        string? openId = null,
        string[]? roles = null,
        string? tenantId = null,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var sessionKey = GenerateSessionKey(clientId, appId);
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry ?? _defaultExpiry).ToUnixTimeMilliseconds();

        var session = new EdgeSession
        {
            SessionKey = sessionKey,
            Token = token,
            UserId = userId,
            MemberId = memberId,
            OpenId = openId,
            Roles = roles ?? [],
            TenantId = tenantId,
            ExpiresAt = expiresAt,
        };

        await _store.SaveAsync(session, ct);
        return sessionKey;
    }

    /// <summary>Validates a sessionKey; sliding renewal on success.</summary>
    public async Task<EdgeSession?> ValidateAsync(string sessionKey, bool autoRenew = true, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionKey))
            return null;

        var session = await _store.GetAsync(sessionKey, ct);
        if (session is null || session.IsExpired)
            return null;

        if (autoRenew)
            await _store.RenewAsync(sessionKey, _defaultExpiry, ct);

        return session;
    }

    /// <summary>Actively revokes a session (logout).</summary>
    public async Task RevokeAsync(string sessionKey, CancellationToken ct = default)
    {
        await _store.RemoveAsync(sessionKey, ct);
    }

    /// <summary>Generates a sessionKey: {clientId}:{appId}:{guid}.</summary>
    public static string GenerateSessionKey(string clientId, string appId)
    {
        return $"{clientId}:{appId}:{Guid.NewGuid():N}";
    }
}

/// <summary>
/// DI extensions for Edge sessions.
/// </summary>
public static class EdgeSessionExtensions
{
    /// <summary>Registers the in-memory session store (single-instance deployments).</summary>
    public static IServiceCollection AddEdgeInMemorySession(this IServiceCollection services, TimeSpan? defaultExpiry = null)
    {
        services.AddSingleton<InMemoryEdgeSessionStore>();
        services.AddSingleton<IEdgeSessionStore>(sp => sp.GetRequiredService<InMemoryEdgeSessionStore>());
        services.AddSingleton(sp => new EdgeSessionManager(sp.GetRequiredService<IEdgeSessionStore>(), defaultExpiry));
        return services;
    }

    /// <summary>Registers a custom session store (e.g. a distributed cache-backed implementation).</summary>
    public static IServiceCollection AddEdgeSession<TStore>(this IServiceCollection services, TimeSpan? defaultExpiry = null)
        where TStore : class, IEdgeSessionStore
    {
        services.AddSingleton<IEdgeSessionStore, TStore>();
        services.AddSingleton(sp => new EdgeSessionManager(sp.GetRequiredService<IEdgeSessionStore>(), defaultExpiry));
        return services;
    }
}
