
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
    public int ValidateWindowSeconds { get; set; } = 60;
    public int ValidateFailureLimit { get; set; } = 50;
    public string ValidatePathPrefixes { get; set; } = "/api/auth/validate";
    public int HealthPermitLimit { get; set; } = 60;
    public int HealthWindowSeconds { get; set; } = 60;
    public string HealthPathPrefixes { get; set; } = "/health";
    public bool FailureRateLimitEnabled { get; set; } = true;
    public int FailurePermitLimit { get; set; } = 50;
    public int FailureWindowSeconds { get; set; } = 60;
    public bool NotFoundRateLimitEnabled { get; set; } = true;
    public int NotFoundPermitLimit { get; set; } = 50;
    public int NotFoundWindowSeconds { get; set; } = 60;
}

public static class GatewayRateLimitingExtensions
{
    public static IServiceCollection AddPubSoftGatewayRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.ForwardLimit = null;
        });

        var options = configuration.GetSection(GatewayRateLimitOptions.SectionName).Get<GatewayRateLimitOptions>()
                      ?? new GatewayRateLimitOptions();
        if (!options.Enabled) return services;

        services.Configure<GatewayRateLimitOptions>(o =>
        {
            o.GetType().GetProperties().ToList().ForEach(p => p.SetValue(o, p.GetValue(options)));
        });

        var loginPrefixes = SplitPrefixes(options.LoginPathPrefixes);
        var oauthPrefixes = SplitPrefixes(options.OAuthPathPrefixes);
        var validatePrefixes = SplitPrefixes(options.ValidatePathPrefixes);
        var healthPrefixes = SplitPrefixes(options.HealthPathPrefixes);
        var anySpecial = loginPrefixes.Concat(oauthPrefixes).Concat(validatePrefixes).ToList();

        Add(services, "login", options.LoginPermitLimit, options.LoginWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), loginPrefixes),
            ctx => $"login:{Ip(ctx)}");

        Add(services, "oauth", options.OAuthPermitLimit, options.OAuthWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), oauthPrefixes),
            ctx => ClientId(ctx) is { } c ? $"oauth:client:{c}@{Ip(ctx)}" : $"oauth:{Ip(ctx)}");

        Add(services, "validate-client", options.ValidatePermitLimit, options.ValidateWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), validatePrefixes) && ClientId(ctx) != null,
            ctx => $"validate:client:{ClientId(ctx)}@{Ip(ctx)}");

        Add(services, "validate-anon", options.ValidateAnonymousPermitLimit, options.ValidateWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), validatePrefixes) && ClientId(ctx) == null,
            ctx => $"validate:{Ip(ctx)}");

        Add(services, "health", options.HealthPermitLimit, options.HealthWindowSeconds,
            ctx => MatchesPrefix(Path(ctx), healthPrefixes),
            ctx => $"health:{Ip(ctx)}");

        Add(services, "default", options.PermitLimit, options.WindowSeconds,
            ctx => !MatchesPrefix(Path(ctx), anySpecial),
            ctx => options.PerEndpoint ? $"{Identity(ctx)}:{EndpointGroup(Path(ctx))}" : Identity(ctx));

        if (options.FailureRateLimitEnabled)
        {
            AddFailure(services, "fail-oauth", options.OAuthFailureLimit, options.OAuthWindowSeconds,
                ctx => IsApi(ctx) && MatchesPrefix(Path(ctx), oauthPrefixes));

            AddFailure(services, "fail-validate", options.ValidateFailureLimit, options.ValidateWindowSeconds,
                ctx => IsApi(ctx) && MatchesPrefix(Path(ctx), validatePrefixes));

            AddFailure(services, "fail-default", options.FailurePermitLimit, options.FailureWindowSeconds,
                ctx => IsApi(ctx) && !MatchesPrefix(Path(ctx), oauthPrefixes) && !MatchesPrefix(Path(ctx), validatePrefixes));

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
                    o.IdentifierSelector = ctx => $"nf:{Identity(ctx)}";
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
        Func<HttpContext, bool> appliesTo)
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
            o.IdentifierSelector = ctx => $"{Identity(ctx)}:{EndpointGroup(Path(ctx))}";
        });

    internal static string Path(HttpContext context) => context.Request.Path.Value ?? "/";

    internal static string Ip(HttpContext context)
    {
        var ip = IpNormalizer.GetRemoteIp(context);
        return string.IsNullOrEmpty(ip) ? "unknown" : ip;
    }

    internal static string Identity(HttpContext context)
        => ClientId(context) is { } c ? $"{c}@{Ip(context)}" : Ip(context);

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
}
