using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            services.Configure<JwksOptions>(authServerSection);
            services.AddHttpClient<JwksKeyProvider>();
            jwksOptions = authServerSection.Get<JwksOptions>() ?? new JwksOptions();
            if (string.IsNullOrWhiteSpace(jwksOptions.JwksUri) && !string.IsNullOrWhiteSpace(jwksOptions.Issuer))
                jwksOptions.JwksUri = $"{jwksOptions.Issuer.TrimEnd('/')}/.well-known/jwks.json";
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
            authBuilder.AddJwtBearer(AuthServerScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwksOptions.Issuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    IssuerSigningKeyResolver = (_, _, kid, _) =>
                    {
                        var provider = services.BuildServiceProvider().GetRequiredService<JwksKeyProvider>();
                        var key = provider.ResolveSigningKeyAsync(kid ?? "").GetAwaiter().GetResult();
                        return key is null ? Array.Empty<SecurityKey>() : new[] { key };
                    },
                };
            });
        }

        return services;
    }

    private static string PadBase64(string base64) =>
        base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')
            .Replace('-', '+')
            .Replace('_', '/');
}
