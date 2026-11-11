using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>DI registration for the EF-backed security stores.</summary>
public static class SecurityEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Swaps <see cref="IRefreshTokenFamilyStore"/> for the EF-backed implementation on
    /// <typeparamref name="TDbContext"/>. Requires the host DbContext registered, the tables
    /// mapped via <see cref="SecurityStateModelBuilderExtensions.ConfigurePubSoftSecurityState"/>,
    /// and a registered <see cref="PubSoft.Core.IIdGenerator"/>.
    /// </summary>
    public static IServiceCollection AddPubSoftEfCoreRefreshTokenFamilyStore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IRefreshTokenFamilyStore, EfCoreRefreshTokenFamilyStore<TDbContext>>());
        return services;
    }

    /// <summary>
    /// Swaps <see cref="ITokenDenylist"/> for the EF-backed implementation on
    /// <typeparamref name="TDbContext"/>. Same requirements as the refresh family store.
    /// </summary>
    public static IServiceCollection AddPubSoftEfCoreTokenDenylist<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<ITokenDenylist, EfCoreTokenDenylist<TDbContext>>());
        return services;
    }
}
