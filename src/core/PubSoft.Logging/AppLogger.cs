using System.Runtime.CompilerServices;

namespace PubSoft.Logging;

/// <summary>Batched async logger; fans entries out to registered sinks.</summary>
public sealed class AppLogger : AsyncBackbone<LogEntry>
{
    private readonly ILogSink[] _sinks;
    private readonly SinkTracking[] _sinkStates;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private int _minLevel;
    private volatile LogLevelOverride[] _levelOverrides = [];

    /// <summary>Creates the logger.</summary>
    public AppLogger(
        IEnumerable<ILogSink> sinks,
        AppLogLevel minLevel = AppLogLevel.Info,
        int capacity = 8192,
        int batchSize = 128,
        LogSinkQuarantineOptions? quarantineOptions = null,
        TimeProvider? timeProvider = null)
        : this(ValidateOptions(sinks, minLevel), capacity, batchSize, quarantineOptions ?? new LogSinkQuarantineOptions(), timeProvider)
    {
    }

    private AppLogger(ValidatedOptions options, int capacity, int batchSize, LogSinkQuarantineOptions quarantineOptions, TimeProvider? timeProvider)
        : base(nameof(AppLogger), capacity, batchSize)
    {
        quarantineOptions.Validate(nameof(quarantineOptions));
        _sinks = options.Sinks;
        _sinkStates = options.Sinks
            .Select(sink => new SinkTracking(sink.GetType().Name, quarantineOptions))
            .ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
        Volatile.Write(ref _minLevel, (int)options.MinLevel);
    }

    /// <summary>Minimum level; reads/writes are atomic for hot-tuning.</summary>
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

    /// <summary>
    /// Per-source level overrides, longest-prefix match; the array is copied on assignment and swapped
    /// atomically, so replacing it is the dynamic level-switching primitive -- mutating the caller's
    /// original collection after assignment has no effect. Empty disables overrides.
    /// </summary>
    public IReadOnlyList<LogLevelOverride> LevelOverrides
    {
        get => _levelOverrides;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var copy = value.ToArray();
            if (copy.Any(rule => rule is null))
                throw new ArgumentException("Level overrides must not contain null entries.", nameof(value));
            _levelOverrides = copy!;
        }
    }

    /// <summary>Effective minimum level for a source: longest matching override prefix, else the global minimum.</summary>
    public AppLogLevel GetEffectiveMinLevel(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rules = _levelOverrides;
        if (rules.Length == 0)
            return MinLevel;

        LogLevelOverride? best = null;
        foreach (var rule in rules)
        {
            if (!source.StartsWith(rule.SourcePrefix, StringComparison.Ordinal))
                continue;
            if (best is null || rule.SourcePrefix.Length > best.SourcePrefix.Length)
                best = rule;
        }

        return best?.MinLevel ?? MinLevel;
    }

    /// <summary>Enqueues an entry; returns false when below <see cref="MinLevel"/> or draining.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Log(
        AppLogLevel level,
        string source,
        string message,
        string? exception = null,
        string? traceId = null,
        string? service = null,
        string? userId = null,
        string? environment = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        if (level < GetEffectiveMinLevel(source))
            return true;

        try
        {
            var contextSnapshot = context ?? LogContext.GetCurrentContext();
            // Ambient W3C capture mirrors the auditing pipeline: explicit argument wins.
            // Span id is captured only together with the ambient trace - an explicit upstream
            // trace id must not be paired with a local span that belongs to a different trace.
            string? spanId = null;
            if (traceId is null && System.Diagnostics.Activity.Current is { } activity)
            {
                if (activity.TraceId != default)
                    traceId = activity.TraceId.ToHexString();
                if (activity.SpanId != default)
                    spanId = activity.SpanId.ToHexString();
            }
            return TryPublish(new LogEntry(
                level, source, message, exception, traceId, service: service,
                spanId: spanId,
                userId: userId, environment: environment, context: contextSnapshot));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Logs at Trace level.</summary>
    public bool Trace(string source, string message) => Log(AppLogLevel.Trace, source, message);
    /// <summary>Logs at Debug level.</summary>
    public bool Debug(string source, string message) => Log(AppLogLevel.Debug, source, message);
    /// <summary>Logs at Info level.</summary>
    public bool Info(string source, string message) => Log(AppLogLevel.Info, source, message);
    /// <summary>Logs at Warn level.</summary>
    public bool Warn(string source, string message) => Log(AppLogLevel.Warn, source, message);
    /// <summary>Logs at Error level.</summary>
    public bool Error(string source, string message, Exception? exception = null)
        => Log(AppLogLevel.Error, source, message, exception?.ToString());
    /// <summary>Logs at Fatal level.</summary>
    public bool Fatal(string source, string message, Exception? exception = null)
        => Log(AppLogLevel.Fatal, source, message, exception?.ToString());

    /// <summary>Per-sink diagnostics snapshot - quarantine state, failure counts, dropped batches.</summary>
    public IReadOnlyList<LogSinkState> GetSinkStates()
    {
        var now = _timeProvider.GetUtcNow();
        return _sinkStates.Select(state => state.Snapshot(now)).ToArray();
    }

    protected override async ValueTask ProcessBatchAsync(
        ReadOnlyMemory<LogEntry> batch,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        for (var index = 0; index < _sinks.Length; index++)
        {
            var tracking = _sinkStates[index];
            if (tracking.IsQuarantined(now))
            {
                tracking.RecordSkip();
                continue;
            }

            try
            {
                await _sinks[index].WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                tracking.RecordSuccess();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                tracking.RecordFailure(now);
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

    private sealed class SinkTracking(string sinkName, LogSinkQuarantineOptions options)
    {
        private readonly object _sync = new();
        private readonly string _sinkName = sinkName;
        private int _consecutiveFailures;
        private long _skippedBatches;
        private long _failedBatches;
        private DateTimeOffset? _quarantinedUntil;
        private TimeSpan _nextBackoff = options.InitialBackoff;

        public bool IsQuarantined(DateTimeOffset now)
        {
            lock (_sync)
                return _quarantinedUntil is { } until && now < until;
        }

        public void RecordSkip()
        {
            lock (_sync)
                _skippedBatches++;
        }

        public void RecordSuccess()
        {
            lock (_sync)
            {
                _consecutiveFailures = 0;
                _quarantinedUntil = null;
                _nextBackoff = options.InitialBackoff;
            }
        }

        public void RecordFailure(DateTimeOffset now)
        {
            lock (_sync)
            {
                _consecutiveFailures++;
                _failedBatches++;
                if (_consecutiveFailures < options.FailureThreshold)
                    return;
                _quarantinedUntil = now + _nextBackoff;
                _nextBackoff = _nextBackoff.Ticks > options.MaxBackoff.Ticks / 2
                    ? options.MaxBackoff
                    : TimeSpan.FromTicks(_nextBackoff.Ticks * 2);
            }
        }

        public LogSinkState Snapshot(DateTimeOffset now)
        {
            lock (_sync)
            {
                return new LogSinkState
                {
                    SinkName = _sinkName,
                    ConsecutiveFailures = _consecutiveFailures,
                    IsQuarantined = _quarantinedUntil is { } until && now < until,
                    QuarantinedUntil = _quarantinedUntil,
                    SkippedBatches = _skippedBatches,
                    FailedBatches = _failedBatches,
                };
            }
        }
    }

    private sealed record ValidatedOptions(ILogSink[] Sinks, AppLogLevel MinLevel);
}