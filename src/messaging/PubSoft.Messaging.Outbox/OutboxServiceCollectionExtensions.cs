using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.EntityFrameworkCore;

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
        services.TryAddScoped<IOutboxPublisher, EfCoreOutboxPublisher<TDbContext>>();
        services.TryAddScoped<IOutboxStore, EfCoreOutboxStore<TDbContext>>();
        services.TryAddScoped<IInboxMessageProcessor, EfCoreInboxMessageProcessor<TDbContext>>();
        services.TryAddScoped<OutboxProcessor>();
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
