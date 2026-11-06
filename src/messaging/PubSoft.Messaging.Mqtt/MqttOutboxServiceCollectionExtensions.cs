using System.Text;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Messaging.Mqtt;


public static class MqttOutboxServiceCollectionExtensions
{
    /// <summary>Registers the MQTT outbox dispatcher as the IOutboxDispatcher implementation.</summary>
    public static IServiceCollection AddPubSoftMqttOutboxDispatcher(
        this IServiceCollection services, Action<MqttOutboxOptions>? configure = null)
    {
        var options = new MqttOutboxOptions();
        configure?.Invoke(options);
        services.AddSingleton(options.CloneAndValidate());
        services.AddSingleton<MqttOutboxDispatcher>();
        services.AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<MqttOutboxDispatcher>());
        return services;
    }
}
