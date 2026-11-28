using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.EntityFrameworkCore;
using PubSoft.MultiTenancy;

namespace PubSoft.Messaging.Outbox;


/// <summary>Registration for the transactional outbox.</summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>Registers the outbox store, publisher, and inbox processor on <typeparamref name="TDbContext"/>. Requires a registered <see cref="PubSoft.Core.IIdGenerator"/>.</summary>
    public static IServiceCollection AddPubSoftOutbox<TDbContext>(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new OutboxOptions();
        configure?.Invoke(options);
        var validatedOptions = options.CloneAndValidate();
        services.TryAddSingleton(validatedOptions);
        services.TryAddScoped<IOutboxPublisher>(provider => new EfCoreOutboxPublisher<TDbContext>(
            provider.GetRequiredService<TDbContext>(),
            provider.GetRequiredService<PubSoft.Core.IIdGenerator>(),
            provider.GetRequiredService<OutboxOptions>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<IOutboxEventSerializer>()));
        services.TryAddScoped<IOutboxStore, EfCoreOutboxStore<TDbContext>>();
        services.TryAddScoped<IInboxMessageProcessor, EfCoreInboxMessageProcessor<TDbContext>>();
        services.TryAddScoped<OutboxProcessor>();
        services.TryAddSingleton<OutboxWorkerStatusTracker>();
        return services;
    }


    /// <summary>
    /// Registers catalog-backed tenant scope enumeration. Requires a scoped <see cref="ITenantAppService"/>
    /// registration and a scoped tenant context resolvable as <see cref="PubSoft.Domain.ScopedTenantContext"/>.
    /// </summary>
    public static IServiceCollection AddPubSoftTenantOutboxScopeProvider(
        this IServiceCollection services,
        Action<TenantOutboxScopeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new TenantOutboxScopeOptions();
        configure?.Invoke(options);
        if (!options.IsValid(out var error))
            throw new ArgumentException(error, nameof(configure));
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOutboxTenantScopeInitializer, ScopedTenantOutboxScopeInitializer>();
        services.TryAddSingleton<IOutboxScopeProvider, TenantOutboxScopeProvider>();
        return services;
    }

    /// <summary>Registers the polling background worker.</summary>
    public static IServiceCollection AddPubSoftOutboxWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, OutboxBackgroundService>());
        return services;
    }
}
