using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Authentication.Jwt;
using PubSoft.Domain;
using PubSoft.Security;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>Client-assertion verifier registration.</summary>
public static class ClientAssertionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JwtClientAssertionVerifier"/> as the host's
    /// <see cref="IClientAssertionVerifier"/>. Requires a registered
    /// <see cref="IClientAssertionKeyProvider"/> (client id -> public key). Uses the registered
    /// <see cref="INonceStore"/> for jti replay rejection, falling back to a bounded process-local
    /// store -- register a shared store for multi-instance deployments.
    /// </summary>
    public static IServiceCollection AddPubSoftJwtClientAssertionVerifier(
        this IServiceCollection services, Action<ClientAssertionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ClientAssertionOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton<IClientAssertionVerifier>(sp => new JwtClientAssertionVerifier(
            sp.GetRequiredService<IClientAssertionKeyProvider>(),
            options,
            sp.GetService<INonceStore>() ?? new MemoryNonceStore()));
        return services;
    }
}
