using System.Diagnostics.CodeAnalysis;
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
/// <summary>Domain event handler registration helpers.</summary>
public static class DomainEventHandlerServiceCollectionExtensions
{
    /// <summary>Registers a scoped domain event handler for <typeparamref name="TEvent"/>.</summary>
    public static IServiceCollection AddPubSoftDomainEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<THandler>();
        services.AddScoped<IDomainEventHandler<TEvent>, THandler>();
        return services;
    }
}
