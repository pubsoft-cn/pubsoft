using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.Edge;

/// <summary>Remote edge sign-key provider registration.</summary>
public static class RemoteEdgeSignKeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="RemoteEdgeSignKeyProvider"/> as both
    /// <see cref="IEdgeSignKeyProvider"/> and <see cref="IEdgeSchemePolicy"/>.
    /// Options validate at registration: <see cref="RemoteEdgeSignKeyOptions.FetchKeys"/> is required.
    /// </summary>
    public static IServiceCollection AddRemoteEdgeSignKeys(
        this IServiceCollection services, Action<RemoteEdgeSignKeyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RemoteEdgeSignKeyOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(sp =>
            new RemoteEdgeSignKeyProvider(options, sp.GetService<ILogger<RemoteEdgeSignKeyProvider>>()));
        services.TryAddSingleton<IEdgeSignKeyProvider>(sp => sp.GetRequiredService<RemoteEdgeSignKeyProvider>());
        services.TryAddSingleton<IEdgeSchemePolicy>(sp => sp.GetRequiredService<RemoteEdgeSignKeyProvider>());
        return services;
    }
}
