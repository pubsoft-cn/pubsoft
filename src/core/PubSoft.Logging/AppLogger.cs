using System.Runtime.CompilerServices;

namespace PubSoft.Logging;

public sealed class AppLogger : AsyncBackbone<LogEntry>
{
    private readonly ILogSink[] _sinks;
    private int _minLevel;

    public AppLogger(
        IEnumerable<ILogSink> sinks,
        AppLogLevel minLevel = AppLogLevel.Info,
        int capacity = 8192,
        int batchSize = 128)
        : this(ValidateOptions(sinks, minLevel), capacity, batchSize)
    {
    }

    private AppLogger(ValidatedOptions options, int capacity, int batchSize)
        : base(nameof(AppLogger), capacity, batchSize)
    {
        _sinks = options.Sinks;
        Volatile.Write(ref _minLevel, (int)options.MinLevel);
    }

    public AppLogLevel MinLevel
    {
        get => (AppLogLevel)Volatile.Read(ref _minLevel);
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _minLevel, (int)value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Log(
        AppLogLevel level,
        string source,
        string message,
        string? exception = null,
        long traceId = 0,
        string? service = null,
        string? userId = null,
        string? environment = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        if (level < MinLevel)
            return true;

        try
        {
            var contextSnapshot = context ?? LogContext.GetCurrentContext();
            return TryPublish(new LogEntry(
                level, source, message, exception, traceId, service: service,
                userId: userId, environment: environment, context: contextSnapshot));
        }
        catch
        {
            return false;
        }
    }

    public bool Trace(string source, string message) => Log(AppLogLevel.Trace, source, message);
    public bool Debug(string source, string message) => Log(AppLogLevel.Debug, source, message);
    public bool Info(string source, string message) => Log(AppLogLevel.Info, source, message);
    public bool Warn(string source, string message) => Log(AppLogLevel.Warn, source, message);
    public bool Error(string source, string message, Exception? exception = null)
        => Log(AppLogLevel.Error, source, message, exception?.ToString());
    public bool Fatal(string source, string message, Exception? exception = null)
        => Log(AppLogLevel.Fatal, source, message, exception?.ToString());

    protected override async ValueTask ProcessBatchAsync(
        ReadOnlyMemory<LogEntry> batch,
        CancellationToken cancellationToken)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                OnProcessingError(exception, batch.Length);
            }
        }
    }

    private static ValidatedOptions ValidateOptions(IEnumerable<ILogSink> sinks, AppLogLevel minLevel)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        if (!Enum.IsDefined(minLevel))
            throw new ArgumentOutOfRangeException(nameof(minLevel));

        var sinkArray = sinks.ToArray();
        if (sinkArray.Any(sink => sink is null))
            throw new ArgumentException("Log sinks must not contain null entries.", nameof(sinks));
        return new ValidatedOptions(sinkArray, minLevel);
    }

    private sealed record ValidatedOptions(ILogSink[] Sinks, AppLogLevel MinLevel);
}