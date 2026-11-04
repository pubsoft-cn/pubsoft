using PubSoft.Logging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingWave2Tests
{
    private sealed class Sink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];
        public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> batch, CancellationToken ct)
        {
            lock (Entries) Entries.AddRange(batch.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    private static AppLogger Logger(Sink s, AppLogLevel min = AppLogLevel.Trace) => new([s], min, 8, 100);

    [Fact]
    public async Task MinLevelFiltersLowSeverity()
    {
        var s = new Sink();
        await using var l = Logger(s, AppLogLevel.Warn);
        Assert.True(l.Trace("src", "trace")); // filtered = "handled", not enqueued
        Assert.True(l.Info("src", "info"));
        Assert.True(l.Warn("src", "warn"));
        Assert.True(l.Error("src", "err"));
        await l.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, s.Entries.Count); // only warn+error reached the sink
        Assert.DoesNotContain(s.Entries, e => e.Level < AppLogLevel.Warn);
    }

    [Fact]
    public void MinLevelSetterValidates()
    {
        var l = Logger(new Sink());
        l.MinLevel = AppLogLevel.Error;
        Assert.Equal(AppLogLevel.Error, l.MinLevel);
        Assert.Throws<ArgumentOutOfRangeException>(() => l.MinLevel = unchecked((AppLogLevel)999));
    }

    [Fact]
    public async Task SinkReceivesEntriesWithContext()
    {
        var s = new Sink();
        await using var l = Logger(s);
        using (LogContext.PushProperty("userId", 42))
            l.Info("src", "with-ctx");
        await l.DrainAsync(TimeSpan.FromSeconds(5));
        var e = Assert.Single(s.Entries);
        Assert.Equal("src", e.Source);
        Assert.Equal("with-ctx", e.Message);
        Assert.NotNull(e.Context);
        Assert.Equal(42, e.Context!["userId"]);
    }

    [Fact]
    public void LogContextScopeRestore()
    {
        using (LogContext.PushProperty("a", 1))
        {
            Assert.Equal(1, LogContext.GetCurrentContext()!["a"]);
            using (LogContext.PushProperty("b", 2))
            {
                Assert.Equal(2, LogContext.GetCurrentContext()!["b"]);
            }
            Assert.False(LogContext.GetCurrentContext()!.ContainsKey("b")); // scope restored
        }
        Assert.Null(LogContext.GetCurrentContext());
    }
}
