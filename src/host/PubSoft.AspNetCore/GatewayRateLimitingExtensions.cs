
using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PubSoft.AspNetCore;

public sealed class GatewayRateLimitOptions
{
    public const string SectionName = "Gateway:RateLimit";
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 200;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
    public bool PerEndpoint { get; set; } = true;
    public int LoginPermitLimit { get; set; } = 20;
    public int LoginWindowSeconds { get; set; } = 60;
    public string LoginPathPrefixes { get; set; } = "/api/auth/admin-login,/api/auth/refresh-token,/api/auth/revoke";
    public int OAuthPermitLimit { get; set; } = 2000;
    public int OAuthWindowSeconds { get; set; } = 60;
    public int OAuthFailureLimit { get; set; } = 30;
    public string OAuthPathPrefixes { get; set; } = "/api/auth/oauth/relay/exchange,/api/auth/oauth/relay/exchange-admin,/api/auth/oauth/relay/mini-program";
    public int ValidatePermitLimit { get; set; } = 2000;
    public int ValidateAnonymousPermitLimit { get; set; } = 10;
    /// <summary>Aggregate per-IP ceiling for validate routes — applies to every request,
    /// with or without a client identity, so forged client IDs cannot multiply the total.</summary>
    public int ValidatePerIpPermitLimit { get; set; } = 1200;
    public int ValidateWindowSeconds { get; set; } = 60;
    public int ValidateFailureLimit { get; set; } = 50;
    public string ValidatePathPrefixes { get; set; } = "/api/auth/validate";
    /// <summary>Aggregate per-IP ceiling for OAuth routes — same anti-partitioning role as
    /// <see cref="ValidatePerIpPermitLimit"/>.</summary>
    public int OAuthPerIpPermitLimit { get; set; } = 600;
    public int HealthPermitLimit { get; set; } = 60;
    public int HealthWindowSeconds { get; set; } = 60;
    public string HealthPathPrefixes { get; set; } = "/health";
    public bool FailureRateLimitEnabled { get; set; } = true;
    public int FailurePermitLimit { get; set; } = 50;
    public int FailureWindowSeconds { get; set; } = 60;
    public bool NotFoundRateLimitEnabled { get; set; } = true;
    public int NotFoundPermitLimit { get; set; } = 50;
    public int NotFoundWindowSeconds { get; set; } = 60;
    /// <summary>Max distinct client identities one IP may claim inside
    /// <see cref="ClientIdWindowSeconds"/>. Beyond the cap the request is bucketed by IP
    /// alone — rotating X-Client-Id cannot keep minting fresh limiter partitions.</summary>
    public int MaxClientIdsPerIp { get; set; } = 32;
    public int ClientIdWindowSeconds { get; set; } = 300;
    /// <summary>Upper bound on IPs tracked for client-ID cardinality. At capacity, unseen
    /// IPs fall back to IP-only bucketing.</summary>
    public int MaxTrackedIps { get; set; } = 10_000;
    /// <summary>Trusted reverse-proxy socket peers (IPv4/IPv6). Combined with
    /// <see cref="TrustedNetworks"/>: when either is non-empty, only listed peers may
    /// supply forwarded headers. When both are empty every peer is trusted — safe only
    /// when the backend socket is reachable exclusively through the edge gateway.
    /// Invalid entries fail startup.</summary>
    public string[] TrustedProxies { get; set; } = [];
    /// <summary>Trusted proxy networks in CIDR notation ("172.16.0.0/12"). Invalid entries fail startup.</summary>
    public string[] TrustedNetworks { get; set; } = [];
    /// <summary>Max X-Forwarded-For entries consumed from the right. Null = process all entries.</summary>
    public int? ForwardLimit { get; set; }
}

public static class GatewayRateLimitingExtensions
{
    public static IServiceCollection AddPubSoftGatewayRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(GatewayRateLimitOptions.SectionName).Get<GatewayRateLimitOptions>()
                      ?? new GatewayRateLimitOptions();
        if (!options.Enabled) return services;

        // 仅在启用时限流时重写转发头信任面——否则限流关闭也会信任任意 X-Forwarded-For。
        // 未配置 TrustedProxies/TrustedNetworks 时信任任意上游 peer——适用前提：后端
        // socket 只能经可信网关到达。配置了信任清单则只接受列出的代理对端。
        var trustedProxies = ParseAddresses(options.TrustedProxies, nameof(options.TrustedProxies));
        var trustedNetworks = ParseNetworks(options.TrustedNetworks, nameof(options.TrustedNetworks));
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (trustedProxies.Count == 0 && trustedNetworks.Count == 0)
            {
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            }
            else
            {
                foreach (var proxy in trustedProxies)
                    o.KnownProxies.Add(proxy);
                foreach (var network in trustedNetworks)
                    o.KnownIPNetworks.Add(network);
            }
            o.ForwardLimit = options.ForwardLimit;
        });

        services.Configure<GatewayRateLimitOptions>(
            configuration.GetSection(GatewayRateLimitOptions.SectionName));

        var loginPrefixes = SplitPrefixes(options.LoginPathPrefixes);
        var oauthPrefixes = SplitPrefixes(options.OAuthPathPrefixes);
        var validatePrefixes = SplitPrefixes(options.ValidatePathPrefixes);
        var healthPrefixes = SplitPrefixes(options.HealthPathPrefixes);
        var anySpecial = loginPrefixes.Concat(oauthPrefixes).Concat(validatePrefixes).ToList();

        Add(services, "login", options.LoginPermitLimit, options.LoginWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), loginPrefixes),
            ctx => $"login:{Ip(ctx)}");

        // X-Client-Id / Basic 用户名都是未认证输入——旋转取值原本能为每个伪造 clientId
        // 开出独立限流分区。两层防线：guard 限制单 IP 可声明的 distinct clientId，超额
        // 退回按 IP 桶；*-ip 聚合策略对所有匹配请求无条件按 IP 计数，封住总配额。
        var clientIdGuard = new ClientIdCardinalityGuard(
            options.MaxClientIdsPerIp, options.MaxTrackedIps,
            TimeSpan.FromSeconds(Math.Max(1, options.ClientIdWindowSeconds)));

        Add(services, "oauth-ip", options.OAuthPerIpPermitLimit, options.OAuthWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), oauthPrefixes),
            ctx => $"oauth-ip:{Ip(ctx)}");

        Add(services, "oauth", options.OAuthPermitLimit, options.OAuthWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), oauthPrefixes),
            ctx => TrustedClientId(ctx, clientIdGuard) is { } c ? $"oauth:client:{c}@{Ip(ctx)}" : $"oauth:{Ip(ctx)}");

        Add(services, "validate-ip", options.ValidatePerIpPermitLimit, options.ValidateWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), validatePrefixes),
            ctx => $"validate-ip:{Ip(ctx)}");

        Add(services, "validate-client", options.ValidatePermitLimit, options.ValidateWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), validatePrefixes) && TrustedClientId(ctx, clientIdGuard) != null,
            ctx => $"validate:client:{TrustedClientId(ctx, clientIdGuard)}@{Ip(ctx)}");

        Add(services, "validate-anon", options.ValidateAnonymousPermitLimit, options.ValidateWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), validatePrefixes) && TrustedClientId(ctx, clientIdGuard) == null,
            ctx => $"validate:{Ip(ctx)}");

        Add(services, "health", options.HealthPermitLimit, options.HealthWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), healthPrefixes),
            ctx => $"health:{Ip(ctx)}");

        Add(services, "default", options.PermitLimit, options.WindowSeconds,
            ctx => !MatchesPrefix(Path(ctx), anySpecial),
            ctx => options.PerEndpoint ? $"{Identity(ctx, clientIdGuard)}:{EndpointGroup(Path(ctx))}" : Identity(ctx, clientIdGuard));

        if (options.FailureRateLimitEnabled)
        {
            AddFailure(services, "fail-oauth", options.OAuthFailureLimit, options.OAuthWindowSeconds,
                ctx => IsApi(ctx) && MatchesPrefix(Path(ctx), oauthPrefixes), clientIdGuard);

            AddFailure(services, "fail-validate", options.ValidateFailureLimit, options.ValidateWindowSeconds,
                ctx => IsApi(ctx) && MatchesPrefix(Path(ctx), validatePrefixes), clientIdGuard);

            AddFailure(services, "fail-default", options.FailurePermitLimit, options.FailureWindowSeconds,
                ctx => IsApi(ctx) && !MatchesPrefix(Path(ctx), oauthPrefixes) && !MatchesPrefix(Path(ctx), validatePrefixes),
                clientIdGuard);

            if (options.NotFoundRateLimitEnabled)
            {
                services.AddPubSoftRateLimitPolicy(o =>
                {
                    o.Name = "fail-notfound";
                    o.RequestsPerWindow = options.NotFoundPermitLimit;
                    o.WindowSize = TimeSpan.FromSeconds(Math.Max(1, options.NotFoundWindowSeconds));
                    o.CountFailuresOnly = true;
                    o.IsFailureResponse = (_, status) => status == StatusCodes.Status404NotFound;
                    o.AppliesTo = ctx => IsApi(ctx) && ctx.GetEndpoint() == null;
                    o.IdentifierSelector = ctx => $"nf:{Identity(ctx, clientIdGuard)}";
                });
            }
        }

        return services;
    }

    public static IApplicationBuilder UsePubSoftGatewayRateLimiting(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<GatewayRateLimitOptions>>().Value;
        if (!options.Enabled) return app;

        app.UseForwardedHeaders();
        app.UsePubSoftRateLimitPolicies();
        return app;
    }

    private static void Add(
        IServiceCollection services, string name, int limit, int windowSec,
        Func<HttpContext, bool> appliesTo, Func<HttpContext, string> identifier)
        => services.AddPubSoftRateLimitPolicy(o =>
        {
            o.Name = name;
            o.RequestsPerWindow = limit;
            o.WindowSize = TimeSpan.FromSeconds(Math.Max(1, windowSec));
            o.AppliesTo = appliesTo;
            o.IdentifierSelector = identifier;
        });

    private static void AddFailure(
        IServiceCollection services, string name, int limit, int windowSec,
        Func<HttpContext, bool> appliesTo, ClientIdCardinalityGuard guard)
        => services.AddPubSoftRateLimitPolicy(o =>
        {
            o.Name = name;
            o.RequestsPerWindow = limit;
            o.WindowSize = TimeSpan.FromSeconds(Math.Max(1, windowSec));
            o.CountFailuresOnly = true;
            o.IsFailureResponse = (ctx, status) =>
                status >= 400 && status != StatusCodes.Status429TooManyRequests
                && !(status == StatusCodes.Status401Unauthorized
                     && ctx.Request.Headers.ContainsKey("Authorization"));
            o.AppliesTo = appliesTo;
            o.IdentifierSelector = ctx => $"{Identity(ctx, guard)}:{EndpointGroup(Path(ctx))}";
        });

    internal static string Path(HttpContext context) => context.Request.Path.Value ?? "/";

    internal static string Ip(HttpContext context)
    {
        var ip = IpNormalizer.GetRemoteIp(context);
        return string.IsNullOrEmpty(ip) ? "unknown" : ip;
    }

    internal static string Identity(HttpContext context, ClientIdCardinalityGuard guard)
        => TrustedClientId(context, guard) is { } c ? $"{c}@{Ip(context)}" : Ip(context);

    private static string? TrustedClientId(HttpContext context, ClientIdCardinalityGuard guard)
        => ClientId(context) is { } c && guard.Admit(Ip(context), c) ? c : null;

    private static bool IsApi(HttpContext context)
        => Path(context).StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    internal static string? ClientId(HttpContext context)
    {
        var clientId = context.Request.Headers["X-Client-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(clientId))
            return clientId.Trim();

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var encoded = authHeader["Basic ".Length..].Trim();
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var parts = decoded.Split(':', 2);
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                    return parts[0].Trim();
            }
            catch { }
        }

        return null;
    }

    private static List<string> SplitPrefixes(string? csv)
        => csv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(p => p.TrimEnd('/')).ToList() ?? new();

    private static bool MatchesPrefix(string path, List<string> prefixes)
        => prefixes.Count > 0 && prefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    internal static string EndpointGroup(string path)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length >= 3 ? $"{segs[1]}/{segs[2]}"
            : segs.Length == 2 ? segs[1]
            : "root";
    }

    private static List<IPAddress> ParseAddresses(string[] entries, string optionName)
    {
        var result = new List<IPAddress>(entries.Length);
        foreach (var entry in entries)
        {
            if (!IPAddress.TryParse(entry, out var address))
                throw new ArgumentException(
                    $"Gateway:RateLimit:{optionName} entry \"{entry}\" is not a valid IP address.", optionName);
            result.Add(address);
        }
        return result;
    }

    private static List<System.Net.IPNetwork> ParseNetworks(string[] entries, string optionName)
    {
        var result = new List<System.Net.IPNetwork>(entries.Length);
        foreach (var entry in entries)
        {
            var parts = entry.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var prefix)
                || !int.TryParse(parts[1], out var prefixLength))
                throw new ArgumentException(
                    $"Gateway:RateLimit:{optionName} entry \"{entry}\" is not valid CIDR notation (\"<address>/<prefix-length>\").", optionName);
            result.Add(new System.Net.IPNetwork(prefix, prefixLength));
        }
        return result;
    }
}

/// <summary>
/// Bounds how many distinct client identities a single remote IP may claim within a
/// window. Client-ID headers are unauthenticated input — without this bound a caller
/// could mint a fresh rate-limit partition per forged value. Over-cap identities are
/// refused so the request falls back to IP-only bucketing.
/// </summary>
internal sealed class ClientIdCardinalityGuard
{
    private sealed class IpWindow
    {
        public DateTimeOffset Start;
        public HashSet<string> ClientIds = new(StringComparer.Ordinal);
    }

    private readonly int _maxClientIdsPerIp;
    private readonly int _maxIps;
    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;
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
}
