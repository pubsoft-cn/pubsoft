using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>Remote client-credential verifier registration.</summary>
public static class RemoteClientCredentialServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RemoteClientCredentialVerifier"/> as the host's
    /// <see cref="IClientCredentialVerifier"/>. Options validate at registration: the endpoint must
    /// be an absolute https URI (loopback plaintext permitted, other plaintext requires
    /// <see cref="RemoteClientCredentialOptions.AllowInsecureEndpoint"/>), timeout 1-15 s, cache 0-60 min.
    /// </summary>
    public static IServiceCollection AddPubSoftRemoteClientCredentialVerifier(
        this IServiceCollection services, Action<RemoteClientCredentialOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RemoteClientCredentialOptions();
        configure(options);
        options.Validate();

        const string httpClientName = "pubsoft-remote-client-credential";
        services.AddHttpClient(httpClientName, client => client.Timeout = options.Timeout);
        services.TryAddSingleton<IClientCredentialVerifier>(sp => new RemoteClientCredentialVerifier(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
            options,
            sp.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<RemoteClientCredentialVerifier>>()));
        return services;
    }
}
