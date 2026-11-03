using PubSoft.Logging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingWave2Tests
{
    private sealed class CaptureSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];
        public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken ct)
        {
            Entries.AddRange(entries.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AppLoggerFiltersBelowMinLevel()
    {
        var sink = new CaptureSink();
        var logger = new AppLogger([sink], AppLogLevel.Warn, capacity: 64);
        // fail-open: filtered levels still return true — only publish failure returns false
        Assert.True(logger.Debug("s", "debug-msg"));
        Assert.True(logger.Info("s", "info-msg"));
        Assert.True(logger.Warn("s", "warn-msg"));
        Assert.True(logger.Error("s", "err-msg"));
        Assert.True(logger.Fatal("s", "fatal-msg", new Exception("x")));
        await logger.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, sink.Entries.Count); // only Warn+ reached the sink
        Assert.DoesNotContain(sink.Entries, e => e.Level < AppLogLevel.Warn);
    }

    [Fact]
    public async Task AppLoggerDrainsToSinks()
    {
        var sink = new CaptureSink();
        var logger = new AppLogger([sink], AppLogLevel.Trace, capacity: 64);
        logger.Info("svc", "one");
        logger.Error("svc", "two", new InvalidOperationException("e"));
        Assert.True(logger.PendingCount >= 0);
        await logger.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, sink.Entries.Count);
        Assert.Equal("one", sink.Entries[0].Message);
        Assert.Equal(AppLogLevel.Error, sink.Entries[1].Level);
        Assert.Equal("svc", sink.Entries[0].Source);
    }

    [Fact]
    public void AppLoggerMinLevelValidates()
    {
        var logger = new AppLogger([new CaptureSink()]);
        logger.MinLevel = AppLogLevel.Debug;
        Assert.Equal(AppLogLevel.Debug, logger.MinLevel);
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.MinLevel = (AppLogLevel)77);
    }

    [Fact]
    public async Task AppLoggerStopsAcceptingAfterDispose()
    {
        var sink = new CaptureSink();
        var logger = new AppLogger([sink], capacity: 64);
        await logger.DisposeAsync();
        Assert.False(logger.Info("s", "post-dispose"));
    }

    [Fact]
    public void AppLoggerAllowsEmptySinks()
    {
        // empty sink list is valid — entries are accepted and dropped
        var logger = new AppLogger([]);
        Assert.True(logger.Info("s", "m"));
    }
}
