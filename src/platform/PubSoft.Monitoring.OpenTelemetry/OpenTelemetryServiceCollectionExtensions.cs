using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PubSoft.Monitoring.OpenTelemetry;

/// <summary>OpenTelemetry registration for PubSoft monitoring signals.</summary>
public static class OpenTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry with the PubSoft monitoring meter, bounded explicit source/meter additions,
    /// optional ASP.NET Core/HTTP client trace instrumentation, and an OTLP exporter only when
    /// <see cref="PubSoftOpenTelemetryOptions.OtlpEndpoint"/> is configured.
    /// </summary>
    public static IServiceCollection AddPubSoftOpenTelemetry(
        this IServiceCollection services,
        Action<PubSoftOpenTelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PubSoftOpenTelemetryOptions();
        configure(options);
        if (!options.IsValid(out var error))
            throw new ArgumentException(error, nameof(configure));

        services.AddPubSoftMonitoringMetrics();
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.ServiceName!,
                serviceVersion: options.ServiceVersion))
            .WithMetrics(metrics => ConfigureMetrics(metrics, options))
            .WithTracing(tracing => ConfigureTracing(tracing, options));
        return services;
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, PubSoftOpenTelemetryOptions options)
    {
        metrics.AddMeter(MonitoringMetrics.MeterName);
        foreach (var meter in options.AdditionalMeters)
            metrics.AddMeter(meter);
        if (options.OtlpEndpoint is not null)
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = options.OtlpEndpoint);
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, PubSoftOpenTelemetryOptions options)
    {
        foreach (var source in options.AdditionalActivitySources)
            tracing.AddSource(source);
        if (options.IncludeAspNetCoreTraceInstrumentation)
            tracing.AddAspNetCoreInstrumentation();
        if (options.IncludeHttpClientTraceInstrumentation)
            tracing.AddHttpClientInstrumentation();
        if (options.OtlpEndpoint is not null)
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = options.OtlpEndpoint);
    }
}
