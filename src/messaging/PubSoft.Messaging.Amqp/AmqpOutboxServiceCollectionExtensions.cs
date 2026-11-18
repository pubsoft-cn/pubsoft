using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Amqp;


/// <summary>AMQP outbox dispatcher registration.</summary>
public static class AmqpOutboxServiceCollectionExtensions
{
    /// <summary>Registers the AMQP (RabbitMQ) outbox dispatcher as the IOutboxDispatcher implementation.</summary>
    public static IServiceCollection AddPubSoftAmqpOutboxDispatcher(
        this IServiceCollection services, Action<AmqpOutboxOptions>? configure = null)
    {
        var options = new AmqpOutboxOptions();
        configure?.Invoke(options);
        services.AddSingleton(options.CloneAndValidate());
        services.AddSingleton(sp => new AmqpOutboxDispatcher(
            options, sp.GetService<ILogger<AmqpOutboxDispatcher>>()));
        services.AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<AmqpOutboxDispatcher>());
        return services;
    }
}
