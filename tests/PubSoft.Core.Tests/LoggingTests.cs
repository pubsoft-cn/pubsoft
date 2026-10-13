using System.Text.Json;
using Microsoft.Extensions.Logging;
using PubSoft.Logging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingTests
{
    [Fact]
    public void AppLoggerValidatesInputsBeforeStartingItsConsumer()
    {
        Assert.Throws<ArgumentNullException>(() => new AppLogger(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppLogger([], (AppLogLevel)99));
        Assert.Throws<ArgumentException>(() => new AppLogger([null!]));
    }

    [Fact]
    public void LogEntryCopiesContextAndNormalizesFields()
    {
        var context = new Dictionary<string, object?> { ["tenant"] = "north" };
        var entry = new LogEntry(AppLogLevel.Info, " orders ", " created ", context: context);
        context["tenant"] = "changed";

        Assert.Equal("orders", entry.Source);
        Assert.Equal("created", entry.Message);
        Assert.Equal("north", entry.Context!["tenant"]);
        Assert.Equal(TimeSpan.Zero, entry.TimestampUtc.Offset);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, object?>)entry.Context)["tenant"] = "changed");
    }

    [Fact]
    public void LogContextRestoresNestedScopesAndReturnsSnapshots()
    {
        LogContext.Clear();
        using (LogContext.PushProperty("request", "r-1"))
        {
            var snapshot = Assert.IsType<Dictionary<string, object?>>(LogContext.GetCurrentContext());
            snapshot["request"] = "mutated";
            Assert.Equal("r-1", LogContext.GetCurrentContext()!["request"]);

            using (LogContext.PushProperty("request", "nested"))
                Assert.Equal("nested", LogContext.GetCurrentContext()!["request"]);

            Assert.Equal("r-1", LogContext.GetCurrentContext()!["request"]);
        }

        Assert.Null(LogContext.GetCurrentContext());
    }

    [Fact]
    public async Task JsonSinkWritesValidNdjsonWithStructuredContext()
    {
        byte[]? output = null;
        var sink = new JsonLogSink((bytes, _) =>
        {
            output = bytes.ToArray();
            return ValueTask.CompletedTask;
        });
        var entries = new[]
        {
            new LogEntry(AppLogLevel.Warn, "orders", "created", context:
                new Dictionary<string, object?> { ["attempt"] = 3, ["ok"] = true })
        };

        await sink.WriteBatchAsync(entries, CancellationToken.None);

        var line = Assert.IsType<string>(System.Text.Encoding.UTF8.GetString(output!).TrimEnd('\n'));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("WARN", document.RootElement.GetProperty("level").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("context").GetProperty("attempt").GetInt32());
        Assert.True(document.RootElement.GetProperty("context").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task AppLoggerRejectsOverflowAndDrainsAcceptedEntries()
    {
        var sink = new BlockingSink();
        await using var logger = new AppLogger([sink], AppLogLevel.Trace, capacity: 1, batchSize: 1);

        Assert.True(logger.Info("test", "first"));
        await sink.FirstBatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(logger.Info("test", "second"));
        Assert.False(logger.Info("test", "overflow"));

        var drain = logger.DrainAsync(TimeSpan.FromSeconds(5)).AsTask();
        sink.ReleaseFirstBatch.TrySetResult();
        await drain;

        Assert.Equal(2, sink.ProcessedCount);
    }

    [Fact]
    public async Task MelProviderAddsScopeAndEventMetadata()
    {
        var sink = new CollectingSink();
        await using var appLogger = new AppLogger([sink], AppLogLevel.Trace);
        using var provider = new AppLoggerProvider(appLogger);
        var logger = provider.CreateLogger("orders.handler");

        using (logger.BeginScope(new Dictionary<string, object?> { ["tenant"] = "north" }))
            logger.Log(LogLevel.Information, new EventId(17, "OrderCreated"), "state", null, (_, _) => "created");
        await appLogger.DrainAsync(TimeSpan.FromSeconds(5));

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("orders.handler", entry.Source);
        Assert.Equal("created", entry.Message);
        Assert.Equal("north", entry.Context!["tenant"]);
        Assert.Equal(17, entry.Context["EventId"]);
        Assert.Equal("OrderCreated", entry.Context["EventName"]);
    }

    private sealed class CollectingSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];

        public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
        {
            for (var index = 0; index < entries.Length; index++)
                Entries.Add(entries.Span[index]);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSink : ILogSink
    {
        private int _processedCount;

        public TaskCompletionSource FirstBatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstBatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ProcessedCount => Volatile.Read(ref _processedCount);

        public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _processedCount) == 1)
            {
                FirstBatchStarted.TrySetResult();
                await ReleaseFirstBatch.Task.WaitAsync(cancellationToken);
            }
        }
    }
}