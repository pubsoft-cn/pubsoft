using System.Text.Json;
using PubSoft.Logging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingParityWaveTests
{
    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class CollectSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];
        public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
        {
            lock (Entries) Entries.AddRange(entries.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<LogEntry[]> DrainEntries(AppLogger logger, CollectSink sink)
    {
        await logger.DrainAsync(TimeSpan.FromSeconds(10));
        lock (sink.Entries) return [.. sink.Entries];
    }


    [Fact]
    public async Task LogCapturesAmbientActivityTraceAndSpanIds()
    {
        using var activity = new System.Diagnostics.Activity("test-op")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C);
        activity.Start();
        Assert.NotNull(System.Diagnostics.Activity.Current);

        var sink = new CollectSink();
        var logger = new AppLogger([sink], AppLogLevel.Trace);
        try
        {
            logger.Info("svc", "inside-activity");
            var entries = await DrainEntries(logger, sink);
            var entry = Assert.Single(entries);
            Assert.Equal(activity.TraceId.ToHexString(), entry.TraceId);
            Assert.Equal(activity.SpanId.ToHexString(), entry.SpanId);
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public async Task ExplicitTraceIdWinsOverAmbientActivity()
    {
        using var activity = new System.Diagnostics.Activity("test-op")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C);
        activity.Start();

        var sink = new CollectSink();
        var logger = new AppLogger([sink], AppLogLevel.Trace);
        try
        {
            logger.Log(AppLogLevel.Info, "svc", "explicit", traceId: "upstream-trace");
            var entries = await DrainEntries(logger, sink);
            Assert.Equal("upstream-trace", Assert.Single(entries).TraceId);
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public async Task NoAmbientActivityLeavesTraceIdNull()
    {
        Assert.Null(System.Diagnostics.Activity.Current);
        var sink = new CollectSink();
        var logger = new AppLogger([sink], AppLogLevel.Trace);
        logger.Info("svc", "no-activity");
        var entries = await DrainEntries(logger, sink);
        var entry = Assert.Single(entries);
        Assert.Null(entry.TraceId);
        Assert.Null(entry.SpanId);
    }

    [Fact]
    public async Task NdjsonWritesTraceIdAsStringForJoinWithAudit()
    {
        using var activity = new System.Diagnostics.Activity("test-op")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C);
        activity.Start();

        var lines = new List<string>();
        var jsonSink = new PubSoft.Logging.JsonLogSink((memory, _) =>
        {
            lines.Add(System.Text.Encoding.UTF8.GetString(memory.ToArray()));
            return ValueTask.CompletedTask;
        });
        await using var jsonLogger = new PubSoft.Logging.AppLogger([jsonSink], capacity: 64, batchSize: 8);
        try
        {
            jsonLogger.Info("svc", "json-trace");
            await jsonLogger.DrainAsync(TimeSpan.FromSeconds(10));
            var line = Assert.Single(lines);
            Assert.Contains("\"trace_id\":\"" + activity.TraceId.ToHexString() + "\"", line);
            Assert.DoesNotContain("\"trace_id\":0", line);
        }
        finally
        {
            activity.Stop();
        }
    }

    private static string[] RollFiles(string dir, string prefix)
        => Directory.GetFiles(dir, $"{prefix}-*.log").Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray()!;

    private static async Task WriteOne(RollingFileLogSink sink, string message, CancellationToken cancellationToken = default)
        => await sink.WriteBatchAsync(new ReadOnlyMemory<LogEntry>(
            [new LogEntry(AppLogLevel.Info, "test", message)]), cancellationToken);

    [Fact]
    public async Task RollingSinkWritesNdjsonToDatedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-logtest-{Guid.NewGuid():N}");
        var time = new MutableTime(DateTimeOffset.Parse("2026-11-28T10:00:00Z"));
        try
        {
            var sink = new RollingFileLogSink(dir, "app", timeProvider: time);
            await WriteOne(sink, "hello");

            var files = RollFiles(dir, "app");
            Assert.Single(files);
            Assert.Equal("app-20261128.log", files[0]);
            var line = File.ReadLines(Path.Combine(dir, files[0])).Single();
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("hello", doc.RootElement.GetProperty("message").GetString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RollingSinkRollsOnDateChange()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-logtest-{Guid.NewGuid():N}");
        var time = new MutableTime(DateTimeOffset.Parse("2026-11-28T23:59:59Z"));
        try
        {
            var sink = new RollingFileLogSink(dir, "app", timeProvider: time);
            await WriteOne(sink, "day-one");
            time.Advance(TimeSpan.FromMinutes(2));
            await WriteOne(sink, "day-two");

            var files = RollFiles(dir, "app");
            Assert.Equal(["app-20261128.log", "app-20261129.log"], files);
            Assert.Contains("day-one", File.ReadAllText(Path.Combine(dir, "app-20261128.log")));
            Assert.Contains("day-two", File.ReadAllText(Path.Combine(dir, "app-20261129.log")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RollingSinkRollsOnSizeWithinSameDay()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-logtest-{Guid.NewGuid():N}");
        var time = new MutableTime(DateTimeOffset.Parse("2026-11-28T10:00:00Z"));
        try
        {
            var sink = new RollingFileLogSink(dir, "app", maxFileSizeBytes: 200, timeProvider: time);
            await WriteOne(sink, new string('a', 150));
            await WriteOne(sink, new string('b', 150));
            await WriteOne(sink, new string('c', 150));

            var files = RollFiles(dir, "app");
            Assert.True(files.Length >= 2, $"expected a size roll, got {string.Join(",", files)}");
            Assert.Equal("app-20261128.log", files[0]);
            Assert.StartsWith("app-20261128_001", files[1]);
            Assert.Contains("aaa", File.ReadAllText(Path.Combine(dir, files[0])));
            Assert.DoesNotContain("aaa", File.ReadAllText(Path.Combine(dir, files[1])));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RollingSinkRetainsOnlyNewestFilesByCount()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-logtest-{Guid.NewGuid():N}");
        var time = new MutableTime(DateTimeOffset.Parse("2026-11-25T10:00:00Z"));
        try
        {
            var sink = new RollingFileLogSink(dir, "app", retainedFileCountLimit: 2, timeProvider: time);
            await WriteOne(sink, "day1");
            time.Advance(TimeSpan.FromDays(1));
            await WriteOne(sink, "day2");
            time.Advance(TimeSpan.FromDays(1));
            await WriteOne(sink, "day3");

            var files = RollFiles(dir, "app");
            Assert.Equal(["app-20261126.log", "app-20261127.log"], files);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RollingSinkDropsFilesBeyondAgeLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-logtest-{Guid.NewGuid():N}");
        var time = new MutableTime(DateTimeOffset.Parse("2026-11-25T10:00:00Z"));
        try
        {
            var sink = new RollingFileLogSink(dir, "app", retainedFileAgeLimit: TimeSpan.FromDays(2), timeProvider: time);
            await WriteOne(sink, "day1");
            time.Advance(TimeSpan.FromDays(1));
            await WriteOne(sink, "day2");
            time.Advance(TimeSpan.FromDays(3));
            await WriteOne(sink, "day5");

            var files = RollFiles(dir, "app");
            Assert.DoesNotContain("app-20261125.log", files);
            Assert.Contains("app-20261129.log", files);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RollingSinkResumesSequenceAfterRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pubsoft-logtest-{Guid.NewGuid():N}");
        var time = new MutableTime(DateTimeOffset.Parse("2026-11-28T10:00:00Z"));
        try
        {
            var first = new RollingFileLogSink(dir, "app", maxFileSizeBytes: 200, timeProvider: time);
            await WriteOne(first, new string('a', 150));
            await WriteOne(first, new string('b', 150));

            // a fresh sink instance (process restart) must not overwrite the existing segments
            var second = new RollingFileLogSink(dir, "app", maxFileSizeBytes: 200, timeProvider: time);
            await WriteOne(second, new string('c', 150));

            var content = string.Concat(RollFiles(dir, "app").Select(f => File.ReadAllText(Path.Combine(dir, f))));
            Assert.Contains("aaa", content);
            Assert.Contains("bbb", content);
            Assert.Contains("ccc", content);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task LevelOverrideFiltersByLongestPrefixMatch()
    {
        var sink = new CollectSink();
        var logger = new AppLogger([sink], AppLogLevel.Warn);
        logger.LevelOverrides =
        [
            new LogLevelOverride("Microsoft", AppLogLevel.Error),
            new LogLevelOverride("Microsoft.AspNetCore.Hosting", AppLogLevel.Info),
        ];

        logger.Info("Microsoft.EntityFrameworkCore", "filtered-below-error");
        logger.Debug("Microsoft.AspNetCore.Hosting", "filtered-below-info");
        logger.Info("Microsoft.AspNetCore.Hosting", "kept-by-longer-prefix");
        logger.Info("myapp.service", "filtered-below-global-warn");
        logger.Error("myapp.service", "kept-by-global");

        var entries = await DrainEntries(logger, sink);
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, entry => entry.Message == "kept-by-longer-prefix");
        Assert.Contains(entries, entry => entry.Message == "kept-by-global");
    }

    [Fact]
    public async Task LevelOverridesHotSwapTakesEffectImmediately()
    {
        var sink = new CollectSink();
        var logger = new AppLogger([sink], AppLogLevel.Info);

        logger.Debug("myapp.service", "dropped-by-global-info");

        logger.LevelOverrides = [new LogLevelOverride("myapp", AppLogLevel.Debug)];
        logger.Debug("myapp.service", "kept-after-swap");

        logger.LevelOverrides = [];
        logger.Debug("myapp.service", "dropped-after-clear");

        var entries = await DrainEntries(logger, sink);
        Assert.Single(entries);
        Assert.Equal("kept-after-swap", entries[0].Message);
    }

    [Fact]
    public void LevelOverrideRejectsInvalidRules()
    {
        Assert.Throws<ArgumentException>(() => new LogLevelOverride("", AppLogLevel.Info));
        Assert.Throws<ArgumentException>(() => new LogLevelOverride("  ", AppLogLevel.Info));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogLevelOverride("ok", (AppLogLevel)99));
        var logger = new AppLogger([new CollectSink()]);
        Assert.Throws<ArgumentException>(() => logger.LevelOverrides = [null!]);
    }
}
