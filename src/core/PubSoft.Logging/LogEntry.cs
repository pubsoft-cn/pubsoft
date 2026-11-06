using System.Collections.ObjectModel;

namespace PubSoft.Logging;

public enum AppLogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5
}

public readonly struct LogEntry
{
    public DateTimeOffset TimestampUtc { get; }
    public AppLogLevel Level { get; }
    public string Source { get; }
    public string Message { get; }
    public string? ExceptionDetail { get; }
    public long TraceId { get; }
    public string? Service { get; }
    public string? ServiceVersion { get; }
    public string? ServiceInstanceId { get; }
    public string? SpanId { get; }
    public string? UserId { get; }
    public string? Environment { get; }
    public string? HostName { get; }
    public IReadOnlyDictionary<string, object?>? Context { get; }

    public LogEntry(
        AppLogLevel level,
        string source,
        string message,
        string? exceptionDetail = null,
        long traceId = 0,
        string? service = null,
        string? serviceVersion = null,
        string? serviceInstanceId = null,
        string? spanId = null,
        string? userId = null,
        string? environment = null,
        string? hostName = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        if (!Enum.IsDefined(level))
            throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        TimestampUtc = DateTimeOffset.UtcNow; // pubsoft:exempt PUBSOFT018 "log entry default stamp — set at emission inside infrastructure"
        Level = level;
        Source = source.Trim();
        Message = message.Trim();
        ExceptionDetail = exceptionDetail;
        TraceId = traceId;
        Service = TrimOrNull(service);
        ServiceVersion = TrimOrNull(serviceVersion);
        ServiceInstanceId = TrimOrNull(serviceInstanceId);
        SpanId = TrimOrNull(spanId);
        UserId = TrimOrNull(userId);
        Environment = TrimOrNull(environment);
        HostName = TrimOrNull(hostName);
        Context = context is null
            ? null
            : new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(context, StringComparer.Ordinal));
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}