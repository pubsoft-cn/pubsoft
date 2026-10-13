using PubSoft.Logging;

var sink = new SmokeSink();
await using var logger = new AppLogger([sink], AppLogLevel.Trace, capacity: 8, batchSize: 2);
using (LogContext.PushProperty("consumer", "isolated"))
    logger.Info("consumer-smoke", "package-restored");
await logger.DrainAsync(TimeSpan.FromSeconds(5));

if (sink.Count != 1 || sink.LastMessage != "package-restored")
    throw new InvalidOperationException("The isolated PubSoft.Logging package smoke check failed.");

Console.WriteLine("Isolated PubSoft.Logging consumer smoke check passed.");

sealed class SmokeSink : ILogSink
{
    public int Count { get; private set; }
    public string? LastMessage { get; private set; }

    public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count += entries.Length;
        if (!entries.IsEmpty)
            LastMessage = entries.Span[^1].Message;
        return ValueTask.CompletedTask;
    }
}