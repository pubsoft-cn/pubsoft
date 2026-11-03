using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MonitoringMetricsTests
{
    [Fact]
    public void RecordsWireMetricSignalsWithBoundedDimensions()
    {
        var measurements = new ConcurrentQueue<MetricMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == MonitoringMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue(new MetricMeasurement(instrument.Name, value, CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue(new MetricMeasurement(instrument.Name, value, CopyTags(tags))));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            measurements.Enqueue(new MetricMeasurement(instrument.Name, value, CopyTags(tags))));
        listener.Start();

        using var metrics = new MonitoringMetrics();
        metrics.RecordRequest(503);
        metrics.RecordRequest(700);
        metrics.RecordError(MonitoringErrorCategory.Timeout);
        metrics.RecordError((MonitoringErrorCategory)int.MaxValue);
        metrics.RecordRequestDuration(TimeSpan.FromMilliseconds(250));
        metrics.RecordBackendConnections(4);

        Assert.Contains(measurements, measurement =>
            measurement.Name == "pubsoft.monitoring.requests" && measurement.Tags["status_class"] == "5xx");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "pubsoft.monitoring.requests" && measurement.Tags["status_class"] == "other");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "pubsoft.monitoring.errors" && measurement.Tags["error_category"] == "timeout");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "pubsoft.monitoring.errors" && measurement.Tags["error_category"] == "other");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "pubsoft.monitoring.request.duration" && Math.Abs(measurement.Value - 0.25) < 0.001);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "pubsoft.monitoring.backend.connections" && measurement.Value == 4);
        Assert.All(measurements, measurement =>
        {
            Assert.DoesNotContain("route", measurement.Tags.Keys);
            Assert.DoesNotContain("backend", measurement.Tags.Keys);
        });
    }

    [Fact]
    public void ValidatesMeasurementsAndRegistersAsSingleton()
    {
        using var metrics = new MonitoringMetrics();
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordRequestDuration(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordBackendConnections(-1));

        var services = new ServiceCollection();
        services.AddPubSoftMonitoringMetrics();
        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<MonitoringMetrics>(), provider.GetRequiredService<MonitoringMetrics>());
    }

    private static Dictionary<string, string?> CopyTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            copy[tag.Key] = tag.Value?.ToString();
        return copy;
    }

    private sealed record MetricMeasurement(string Name, double Value, Dictionary<string, string?> Tags)
    {
        public MetricMeasurement(string name, long value, Dictionary<string, string?> tags)
            : this(name, (double)value, tags)
        {
        }

        public MetricMeasurement(string name, int value, Dictionary<string, string?> tags)
            : this(name, (double)value, tags)
        {
        }
    }
}