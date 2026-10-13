using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PubSoft.Events;

public static class DomainEventServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftEvents(
        this IServiceCollection services,
        Action<DomainEventOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new DomainEventOptions();
        configure?.Invoke(options);
        options.ValidateAndGetCapacity();

        services.TryAddSingleton(new DomainEventOptions { Capacity = options.Capacity });
        services.TryAddSingleton<DomainEventDispatcher>();
        services.TryAddSingleton<IDomainEventDispatcher>(provider =>
            provider.GetRequiredService<DomainEventDispatcher>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DomainEventBackgroundService>());
        return services;
    }
}