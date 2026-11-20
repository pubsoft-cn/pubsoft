using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PubSoft.Tracing;

/// <summary>DI registration for the span exporter.</summary>
public static class PubSoftTracingServiceCollectionExtensions
{
    /// <summary>Registers <see cref="PubSoftSpanExporter"/> as a singleton and a hosted
    /// service that resolves it at host start - the listener begins when the host does
    /// and the container drains/disposes it at shutdown. Options are validated eagerly
    /// at registration (fail closed).</summary>
    public static IServiceCollection AddPubSoftTracing(
        this IServiceCollection services, Action<PubSoftTracingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PubSoftTracingOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(provider => new PubSoftSpanExporter(
            provider.GetRequiredService<PubSoftTracingOptions>()));
        services.AddHostedService<PubSoftTracingHostedService>();
        return services;
    }
}

internal sealed class PubSoftTracingHostedService : IHostedService
{
    // Resolving the exporter in the constructor is the point - it forces the singleton
    // (and its listener) to exist when the host starts.
    public PubSoftTracingHostedService(PubSoftSpanExporter exporter) { }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
