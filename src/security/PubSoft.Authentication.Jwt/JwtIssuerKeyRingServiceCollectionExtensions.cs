using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Authentication.Jwt;

/// <summary>Standard issuer key-ring registration.</summary>
public static class JwtIssuerKeyRingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="ConfigJwtIssuerKeyRing"/> from a configuration section:
    /// <c>{section}:ActiveKeyId</c> and <c>{section}:Keys:{keyId}</c> holding RSA private key PEM
    /// values. Validation is fail-closed at registration -- an empty ring, a missing active key,
    /// or unusable PEM throws while the host is still wiring services.
    /// </summary>
    public static IServiceCollection AddPubSoftJwtIssuerKeyRing(
        this IServiceCollection services, IConfiguration configuration, string sectionName = JwtIssuerKeyRingOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionName);
        var activeKeyId = section["ActiveKeyId"];
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in section.GetSection("Keys").GetChildren())
            if (child.Value is { Length: > 0 })
                keys[child.Key] = child.Value;

        var ring = new ConfigJwtIssuerKeyRing(new JwtIssuerKeyRingOptions
        {
            ActiveKeyId = activeKeyId,
            Keys = keys
        });
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IJwtIssuerKeyRing)))
        {
            ring.Dispose();
            return services;
        }
        // Factory registration so the container disposes the ring (and its RSA instances) at shutdown.
        services.AddSingleton<IJwtIssuerKeyRing>(_ => ring);
        return services;
    }
}
