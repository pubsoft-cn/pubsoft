using System.Diagnostics.Metrics;

namespace PubSoft.Monitoring;

public enum MonitoringErrorCategory
{
    Network,
    Timeout,
    Protocol,
    Other
}

public sealed class MonitoringMetrics : IDisposable
{
    public const string MeterName = "PubSoft.Monitoring";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _requests;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _requestDuration;
    private readonly Histogram<int> _backendConnections;

    public MonitoringMetrics()
    {
        _requests = _meter.CreateCounter<long>("pubsoft.monitoring.requests", "requests");
        _errors = _meter.CreateCounter<long>("pubsoft.monitoring.errors", "errors");
        _requestDuration = _meter.CreateHistogram<double>("pubsoft.monitoring.request.duration", "s");
        _backendConnections = _meter.CreateHistogram<int>("pubsoft.monitoring.backend.connections", "connections");
    }

    public void RecordRequest(int statusCode)
        => _requests.Add(1, new KeyValuePair<string, object?>("status_class", GetStatusClass(statusCode)));

    public void RecordError(MonitoringErrorCategory category)
        => _errors.Add(1, new KeyValuePair<string, object?>("error_category", GetErrorCategory(category)));

    public void RecordRequestDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        _requestDuration.Record(duration.TotalSeconds);
    }

    public void RecordBackendConnections(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _backendConnections.Record(count);
    }

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