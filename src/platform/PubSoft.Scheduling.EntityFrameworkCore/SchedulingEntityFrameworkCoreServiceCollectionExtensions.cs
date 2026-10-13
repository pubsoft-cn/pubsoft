using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Scheduling;

namespace PubSoft.Scheduling.EntityFrameworkCore;

public static class SchedulingEntityFrameworkCoreServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftEfCoreSchedulerStateStore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IStateStore, EfCoreStateStore<TDbContext>>());
        return services;
    }
}