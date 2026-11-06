namespace PubSoft.Logging;

/// <summary>Log output target fed batches by <see cref="AppLogger"/>.</summary>
public interface ILogSink
{
    /// <summary>Writes one batch.</summary>
    ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken);
}

/// <summary>Console sink.</summary>
public sealed class ConsoleLogSink : ILogSink
{
    /// <inheritdoc />
    public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries.Span[index];
            var tag = entry.Level switch
            {
                AppLogLevel.Trace => "TRC",
                AppLogLevel.Debug => "DBG",
                AppLogLevel.Info => "INF",
                AppLogLevel.Warn => "WRN",
                AppLogLevel.Error => "ERR",
                AppLogLevel.Fatal => "FTL",
                _ => "???"
            };
            var output = entry.ExceptionDetail is null
                ? $"{entry.TimestampUtc:HH:mm:ss.fff} [{tag}] {entry.Source}: {entry.Message}"
                : $"{entry.TimestampUtc:HH:mm:ss.fff} [{tag}] {entry.Source}: {entry.Message}\n  {entry.ExceptionDetail}";
            if (entry.Level >= AppLogLevel.Error)
                Console.Error.WriteLine(output);
            else
                Console.WriteLine(output);
        }

        return ValueTask.CompletedTask;
    }
}