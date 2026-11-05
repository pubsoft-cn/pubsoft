using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt.AspNetCore;

public sealed class DualJwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretKey { get; set; } = "";
}

public static class DualJwtAuthenticationExtensions
{
    public const string AuthServerScheme = "AuthServerJwks";

    public static IServiceCollection AddPubSoftDualJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(DualJwtOptions.SectionName).Get<DualJwtOptions>()
                         ?? new DualJwtOptions();
        var authServerSection = configuration.GetSection(JwksOptions.SectionName);
        var hasAuthServer = authServerSection.Exists()
            && !string.IsNullOrWhiteSpace(authServerSection["JwksUri"] ?? authServerSection["Issuer"]);

        JwksOptions? jwksOptions = null;
        if (hasAuthServer)
        {
            jwksOptions = authServerSection.Get<JwksOptions>() ?? new JwksOptions();
            if (string.IsNullOrWhiteSpace(jwksOptions.JwksUri) && !string.IsNullOrWhiteSpace(jwksOptions.Issuer))
                jwksOptions.JwksUri = $"{jwksOptions.Issuer.TrimEnd('/')}/.well-known/jwks.json";
            ValidateJwksOptions(jwksOptions);
            services.Configure<JwksOptions>(authServerSection);
            services.AddOptions<JwksOptions>()
                .PostConfigure<Microsoft.Extensions.Logging.ILogger<JwksOptions>>((options, logger) =>
                {
                    if (string.IsNullOrWhiteSpace(options.Audience))
                        logger.LogWarning(
                            "AuthServer:Audience is not configured — 'aud' is not validated on the " +
                            "AuthServerJwks scheme. Any token the issuer mints for other services is " +
                            "accepted here. Set AuthServer:Audience unless this is intentional.");
                });
            services.AddHttpClient(nameof(JwksKeyProvider));
            services.AddSingleton(sp => new JwksKeyProvider(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwksOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JwksKeyProvider>>(),
                sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(nameof(JwksKeyProvider))));
        }

        var authBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var baseKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                ValidAlgorithms = ["HS256"],
                IssuerSigningKey = baseKey,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = "name",
                RoleClaimType = "role",
            };

            if (hasAuthServer)
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    var authHeader = ctx.Request.Headers.Authorization.ToString();
                    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return JwtBearerDefaults.AuthenticationScheme;
                    var token = authHeader["Bearer ".Length..].Trim();
                    var parts = token.Split('.');
                    if (parts.Length < 2) return JwtBearerDefaults.AuthenticationScheme;
                    try
                    {
                        var headerJson = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(parts[0])));
                        using var doc = JsonDocument.Parse(headerJson);
                        if (doc.RootElement.TryGetProperty("alg", out var alg)
                            && alg.GetString() == "RS256")
                            return AuthServerScheme;
                    }
                    catch { }
                    return JwtBearerDefaults.AuthenticationScheme;
                };
            }
        });

        if (hasAuthServer && jwksOptions is not null)
        {
            authBuilder.AddJwtBearer(AuthServerScheme);
            // Resolved through the host container when JwtBearerOptions are created —
            // building a second provider here would split singleton/cache/disposal lifetimes.
            services.AddOptions<JwtBearerOptions>(AuthServerScheme)
                .Configure<JwksKeyProvider>((options, keyProvider) =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwksOptions.Issuer,
                        ValidateAudience = !string.IsNullOrWhiteSpace(jwksOptions.Audience),
                        ValidAudience = string.IsNullOrWhiteSpace(jwksOptions.Audience) ? null : jwksOptions.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidAlgorithms = ["RS256"],
                        ClockSkew = TimeSpan.FromMinutes(1),
                        IssuerSigningKeyResolver = (_, _, kid, _) =>
                        {
                            var key = keyProvider.ResolveSigningKeyAsync(kid ?? "").GetAwaiter().GetResult();
                            return key is null ? Array.Empty<SecurityKey>() : new[] { key };
                        },
                    };
                });
        }

        return services;
    }

    private static void ValidateJwksOptions(JwksOptions options)
    {
        if (options.CacheMinutes <= 0)
            throw new InvalidOperationException("AuthServer:CacheMinutes must be a positive number of minutes.");
        if (!Uri.TryCreate(options.JwksUri, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !IsLoopbackHost(uri.Host) && !options.AllowInsecureJwksUri))
            throw new InvalidOperationException(
                "AuthServer:JwksUri must be an absolute https URI (loopback hosts are permitted for development); " +
                "set AuthServer:AllowInsecureJwksUri = true to allow plaintext metadata on other hosts.");
    }

    private static bool IsLoopbackHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || host is "127.0.0.1" or "::1" or "[::1]";

    private static string PadBase64(string base64) =>
        base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')
            .Replace('-', '+')
            .Replace('_', '/');
}
