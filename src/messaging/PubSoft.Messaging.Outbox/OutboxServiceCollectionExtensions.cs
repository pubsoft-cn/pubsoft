using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public static class OutboxServiceCollectionExtensions
{
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

    public static IServiceCollection AddPubSoftOutboxWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, OutboxBackgroundService>());
        return services;
    }
}
