using System.Collections.ObjectModel;

namespace PubSoft.Logging;

/// <summary>Log severity, ordered.</summary>
public enum AppLogLevel : byte
{
    /// <summary>Finest granularity.</summary>
    Trace = 0,
    /// <summary>Debug detail.</summary>
    Debug = 1,
    /// <summary>Informational.</summary>
    Info = 2,
    /// <summary>Warning.</summary>
    Warn = 3,
    /// <summary>Error.</summary>
    Error = 4,
    /// <summary>Fatal.</summary>
    Fatal = 5
}

/// <summary>One immutable log record produced by <see cref="AppLogger"/>.</summary>
public readonly struct LogEntry
{

    /// <summary>Creates an entry; level/source/message are validated.</summary>
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

        TimestampUtc = DateTimeOffset.UtcNow; // pubsoft:exempt PUBSOFT018 "log entry default stamp - set at emission inside infrastructure"
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
    /// <summary>Emission time (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; }
    /// <summary>Severity.</summary>
    public AppLogLevel Level { get; }
    /// <summary>Source tag (category/service).</summary>
    public string Source { get; }
    /// <summary>Log text.</summary>
    public string Message { get; }
    /// <summary>Exception detail when attached.</summary>
    public string? ExceptionDetail { get; }
    /// <summary>Correlation id.</summary>
    public long TraceId { get; }
    /// <summary>Service name.</summary>
    public string? Service { get; }
    /// <summary>Service version.</summary>
    public string? ServiceVersion { get; }
    /// <summary>Service instance tag.</summary>
    public string? ServiceInstanceId { get; }
    /// <summary>Span id.</summary>
    public string? SpanId { get; }
    /// <summary>Acting user.</summary>
    public string? UserId { get; }
    /// <summary>Deployment environment.</summary>
    public string? Environment { get; }
    /// <summary>Host name.</summary>
    public string? HostName { get; }
    /// <summary>Structured context properties.</summary>
    public IReadOnlyDictionary<string, object?>? Context { get; }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}