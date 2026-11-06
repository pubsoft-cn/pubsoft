using System.Diagnostics.Metrics;

namespace PubSoft.Monitoring;

/// <summary>Error buckets for metrics.</summary>
public enum MonitoringErrorCategory
{
    /// <summary>Transport failure.</summary>
    Network,
    /// <summary>Deadline exceeded.</summary>
    Timeout,
    /// <summary>Wire-level error.</summary>
    Protocol,
    /// <summary>Anything else.</summary>
    Other
}

/// <summary>OpenTelemetry metric instruments for monitoring probes.</summary>
public sealed class MonitoringMetrics : IDisposable
{
    /// <summary>Meter name.</summary>
    public const string MeterName = "PubSoft.Monitoring";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _requests;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _requestDuration;
    private readonly Histogram<int> _backendConnections;

    /// <summary>Creates the instruments.</summary>
    public MonitoringMetrics()
    {
        _requests = _meter.CreateCounter<long>("pubsoft.monitoring.requests", "requests");
        _errors = _meter.CreateCounter<long>("pubsoft.monitoring.errors", "errors");
        _requestDuration = _meter.CreateHistogram<double>("pubsoft.monitoring.request.duration", "s");
        _backendConnections = _meter.CreateHistogram<int>("pubsoft.monitoring.backend.connections", "connections");
    }

    /// <summary>Counts a request by status class.</summary>
    public void RecordRequest(int statusCode)
        => _requests.Add(1, new KeyValuePair<string, object?>("status_class", GetStatusClass(statusCode)));

    /// <summary>Counts an error by category.</summary>
    public void RecordError(MonitoringErrorCategory category)
        => _errors.Add(1, new KeyValuePair<string, object?>("error_category", GetErrorCategory(category)));

    /// <summary>Records a request duration.</summary>
    public void RecordRequestDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        _requestDuration.Record(duration.TotalSeconds);
    }

    /// <summary>Records the backend connection count.</summary>
    public void RecordBackendConnections(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _backendConnections.Record(count);
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private static string GetStatusClass(int statusCode)
        => statusCode is >= 100 and <= 599 ? $"{statusCode / 100}xx" : "other";

    private static string GetErrorCategory(MonitoringErrorCategory category)
        => category switch
        {
            MonitoringErrorCategory.Network => "network",
            MonitoringErrorCategory.Timeout => "timeout",
            MonitoringErrorCategory.Protocol => "protocol",
            _ => "other"
        };
}