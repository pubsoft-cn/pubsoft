using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Authentication.Jwt;

/// <summary>JWKS client-assertion key provider registration.</summary>
public static class JwksClientAssertionKeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="JwksClientAssertionKeyProvider"/> as
    /// <see cref="IClientAssertionKeyProvider"/>. <see cref="JwksClientAssertionKeyOptions.FetchJwks"/>
    /// is required and validated at registration.
    /// </summary>
    public static IServiceCollection AddPubSoftJwksClientAssertionKeyProvider(
        this IServiceCollection services, Action<JwksClientAssertionKeyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JwksClientAssertionKeyOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton<IClientAssertionKeyProvider>(new JwksClientAssertionKeyProvider(options));
        return services;
    }
}
