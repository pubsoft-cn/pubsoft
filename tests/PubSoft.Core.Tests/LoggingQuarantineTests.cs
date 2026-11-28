using PubSoft.Logging;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingQuarantineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Condition was not met within 5 seconds.");
            await Task.Delay(10);
        }
    }

    private sealed class FailingSink : ILogSink
    {
        private int _invocations;
        public int Invocations => Volatile.Read(ref _invocations);
        public bool ShouldFail { get; set; } = true;

        public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            if (ShouldFail)
                throw new InvalidOperationException("sink exploded");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CollectingSink : ILogSink
    {
        private int _batches;
        public int Batches => Volatile.Read(ref _batches);
        public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _batches);
            return ValueTask.CompletedTask;
        }
    }

    private static AppLogger CreateLogger(
        ILogSink[] sinks,
        TestTimeProvider clock,
        LogSinkQuarantineOptions? quarantine = null)
        => new(sinks, AppLogLevel.Trace, capacity: 64, batchSize: 1,
            quarantineOptions: quarantine ?? new LogSinkQuarantineOptions
            {
                FailureThreshold = 3,
                InitialBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(8),
            },
            timeProvider: clock);

    [Fact]
    public async Task FailingSinkIsQuarantinedAfterThresholdAndSkipsBatches()
    {
        var clock = new TestTimeProvider(Start);
        var sink = new FailingSink();
        await using var logger = CreateLogger([sink], clock);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(logger.Info("test", "entry"));
            await WaitForAsync(() => logger.GetSinkStates()[0].ConsecutiveFailures == i + 1);
        }

        var state = Assert.Single(logger.GetSinkStates());
        Assert.True(state.IsQuarantined);
        Assert.Equal(Start + TimeSpan.FromSeconds(1), state.QuarantinedUntil);

        Assert.True(logger.Info("test", "dropped"));
        await WaitForAsync(() => logger.GetSinkStates()[0].SkippedBatches >= 1);
        Assert.Equal(3, sink.Invocations);
    }

    [Fact]
    public async Task QuarantinedSinkProbesAfterBackoffAndSuccessResetsState()
    {
        var clock = new TestTimeProvider(Start);
        var sink = new FailingSink();
        await using var logger = CreateLogger([sink], clock);

        for (var i = 0; i < 3; i++)
        {
            logger.Info("test", "entry");
            await WaitForAsync(() => logger.GetSinkStates()[0].ConsecutiveFailures == i + 1);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        sink.ShouldFail = false;
        logger.Info("test", "probe");
        await WaitForAsync(() => sink.Invocations == 4 && logger.GetSinkStates()[0].ConsecutiveFailures == 0);

        var state = Assert.Single(logger.GetSinkStates());
        Assert.False(state.IsQuarantined);
        Assert.Null(state.QuarantinedUntil);
    }

    [Fact]
    public async Task ProbeFailureReQuarantinesWithDoubledBackoff()
    {
        var clock = new TestTimeProvider(Start);
        var sink = new FailingSink();
        await using var logger = CreateLogger([sink], clock);

        for (var i = 0; i < 3; i++)
        {
            logger.Info("test", "entry");
            await WaitForAsync(() => logger.GetSinkStates()[0].ConsecutiveFailures == i + 1);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        logger.Info("test", "probe");
        await WaitForAsync(() => logger.GetSinkStates()[0].ConsecutiveFailures == 4);

        var state = Assert.Single(logger.GetSinkStates());
        Assert.True(state.IsQuarantined);
        Assert.Equal(Start + TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(2), state.QuarantinedUntil);
    }

    [Fact]
    public async Task QuarantineDoesNotAffectHealthySinks()
    {
        var clock = new TestTimeProvider(Start);
        var failing = new FailingSink();
        var healthy = new CollectingSink();
        await using var logger = CreateLogger([failing, healthy], clock);

        for (var i = 0; i < 4; i++)
        {
            logger.Info("test", "entry");
            await WaitForAsync(() => healthy.Batches == i + 1);
        }

        Assert.Equal(3, failing.Invocations);
        Assert.Equal(4, healthy.Batches);
    }

    [Fact]
    public async Task GetSinkStatesExposesSinkNamesAndFailureCounts()
    {
        var clock = new TestTimeProvider(Start);
        var failing = new FailingSink();
        var healthy = new CollectingSink();
        await using var logger = CreateLogger([failing, healthy], clock);

        logger.Info("test", "entry");
        await WaitForAsync(() => healthy.Batches == 1 && failing.Invocations == 1);

        var states = logger.GetSinkStates();
        Assert.Equal(2, states.Count);
        Assert.Equal(nameof(FailingSink), states[0].SinkName);
        Assert.Equal(1, states[0].ConsecutiveFailures);
        Assert.Equal(nameof(CollectingSink), states[1].SinkName);
        Assert.Equal(0, states[1].ConsecutiveFailures);
    }

    [Fact]
    public void QuarantineOptionsAreValidated()
    {
        var clock = new TestTimeProvider(Start);
        Assert.Throws<ArgumentException>(() => new AppLogger([new CollectingSink()], AppLogLevel.Trace,
            quarantineOptions: new LogSinkQuarantineOptions { FailureThreshold = 0 }));
        Assert.Throws<ArgumentException>(() => new AppLogger([new CollectingSink()], AppLogLevel.Trace,
            quarantineOptions: new LogSinkQuarantineOptions { InitialBackoff = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new AppLogger([new CollectingSink()], AppLogLevel.Trace,
            quarantineOptions: new LogSinkQuarantineOptions { MaxBackoff = TimeSpan.FromMilliseconds(1) }));
    }
}
