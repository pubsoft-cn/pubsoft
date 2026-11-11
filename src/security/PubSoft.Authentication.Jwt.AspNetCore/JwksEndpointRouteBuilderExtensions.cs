using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Authentication.Jwt.AspNetCore;

/// <summary>Public JWKS endpoint mapping for issuer hosts.</summary>
public static class JwksEndpointRouteBuilderExtensions
{
    /// <summary>Default JWKS endpoint path.</summary>
    public const string DefaultPath = "/.well-known/jwks.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Maps a public JWKS endpoint serving the public parameters of the registered
    /// <see cref="IJwtIssuerKeyRing"/>. The document is serialized once at map time -- key rollover
    /// takes effect on redeploy of the ring configuration. The endpoint is anonymous by design and
    /// never emits private key parameters. Fail-closed: mapping without a registered ring throws.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Route pattern for the JWKS document.</param>
    /// <param name="cacheMaxAgeSeconds">
    /// Cache-Control max-age for the response (0-86400, default 60). Keep it short enough that
    /// consumers pick up rotated keys promptly.
    /// </param>
    public static IEndpointConventionBuilder MapPubSoftJwks(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPath,
        int cacheMaxAgeSeconds = 60)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (cacheMaxAgeSeconds < 0 || cacheMaxAgeSeconds > 86_400)
            throw new ArgumentOutOfRangeException(nameof(cacheMaxAgeSeconds));

        var ring = endpoints.ServiceProvider.GetService<IJwtIssuerKeyRing>()
            ?? throw new InvalidOperationException(
                "MapPubSoftJwks requires a registered IJwtIssuerKeyRing -- call AddPubSoftJwtIssuerKeyRing first.");
        var json = JsonSerializer.Serialize(JwtSigning.ExportJwks(ring.PublishedKeys), s_jsonOptions);
        var cacheControl = $"public, max-age={cacheMaxAgeSeconds.ToString(CultureInfo.InvariantCulture)}";

        RequestDelegate handler = context =>
        {
            context.Response.Headers.CacheControl = cacheControl;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(json, context.RequestAborted);
        };
        return endpoints.MapGet(pattern, handler);
    }
}
