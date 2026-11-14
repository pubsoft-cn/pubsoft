using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Scheduling.EntityFrameworkCore;

/// <summary>Registration for the EF-backed scheduler state store.</summary>
public static class SchedulingEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>Swaps <see cref="IStateStore"/> for the EF-backed implementation on <typeparamref name="TDbContext"/>.</summary>
    public static IServiceCollection AddPubSoftEfCoreSchedulerStateStore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IStateStore, EfCoreStateStore<TDbContext>>());
        return services;
    }
}