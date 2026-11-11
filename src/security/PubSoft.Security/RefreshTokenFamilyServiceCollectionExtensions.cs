using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Security;

/// <summary>DI registration for refresh-token family rotation.</summary>
public static class RefreshTokenFamilyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bounded in-process <see cref="IRefreshTokenFamilyStore"/> and
    /// <see cref="RefreshTokenFamilyService"/>. Single-instance deployments only - register a
    /// shared store (e.g. Redis) instead when running multiple instances.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Optional bounded rotation options.</param>
    /// <param name="maxEntries">Hard cap on retained token records.</param>
    public static IServiceCollection AddPubSoftMemoryRefreshTokenFamily(this IServiceCollection services, Action<RefreshTokenFamilyOptions>? configure = null, int maxEntries = 100_000)
    {
        var options = new RefreshTokenFamilyOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton<IRefreshTokenFamilyStore>(_ => new MemoryRefreshTokenFamilyStore(maxEntries));
        services.TryAddSingleton(_ => new RefreshTokenFamilyService(_.GetRequiredService<IRefreshTokenFamilyStore>(), options));
        return services;
    }

    /// <summary>
    /// Registers <see cref="RefreshTokenFamilyService"/> against an already-registered
    /// <see cref="IRefreshTokenFamilyStore"/> (e.g. Redis). Fails closed at resolution when no
    /// store was registered.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Optional bounded rotation options.</param>
    public static IServiceCollection AddPubSoftRefreshTokenFamilyService(this IServiceCollection services, Action<RefreshTokenFamilyOptions>? configure = null)
    {
        var options = new RefreshTokenFamilyOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton(_ => new RefreshTokenFamilyService(_.GetRequiredService<IRefreshTokenFamilyStore>(), options));
        return services;
    }
}
