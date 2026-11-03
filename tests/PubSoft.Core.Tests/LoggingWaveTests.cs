using System.Text;
using System.Text.Json;
using PubSoft.Logging;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LoggingWaveTests
{
    [Fact]
    public void LogEntryTrimsAndValidates()
    {
        var e = new LogEntry(AppLogLevel.Warn, "  src  ", "  msg  ");
        Assert.Equal("src", e.Source);
        Assert.Equal("msg", e.Message);
        Assert.Equal(AppLogLevel.Warn, e.Level);
        Assert.Throws<ArgumentException>(() => new LogEntry(AppLogLevel.Info, "", "m"));
        Assert.Throws<ArgumentException>(() => new LogEntry(AppLogLevel.Info, "s", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogEntry((AppLogLevel)99, "s", "m"));
    }

    [Fact]
    public void AppLogLevelOrdering()
    {
        Assert.True(AppLogLevel.Trace < AppLogLevel.Debug);
        Assert.True(AppLogLevel.Warn < AppLogLevel.Error);
        Assert.Equal((byte)5, (byte)AppLogLevel.Fatal);
    }

    [Fact]
    public async Task JsonLogSinkEmitsStructuredFields()
    {
        var captured = new List<byte[]>();
        var sink = new JsonLogSink((bytes, _) =>
        {
            captured.Add(bytes.ToArray());
            return ValueTask.CompletedTask;
        });
        var entries = new[]
        {
            new LogEntry(AppLogLevel.Info, "svc", "hello", service: "api", userId: "u1"),
            new LogEntry(AppLogLevel.Error, "svc", "boom", exceptionDetail: "stack"),
        };
        await sink.WriteBatchAsync(entries, CancellationToken.None);
        var text = Encoding.UTF8.GetString(captured.SelectMany(b => b).ToArray());
        Assert.Contains("\"level\":\"INFO\"", text);
        Assert.Contains("\"message\":\"hello\"", text);
        Assert.Contains("\"level\":\"ERROR\"", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public async Task JsonLogSinkEmptyBatchWritesNothing()
    {
        var called = false;
        var sink = new JsonLogSink((_, _) => { called = true; return ValueTask.CompletedTask; });
        await sink.WriteBatchAsync(ReadOnlyMemory<LogEntry>.Empty, CancellationToken.None);
        Assert.False(called);
    }

    [Fact]
    public void LogContextPushPropertyScopes()
    {
        LogContext.Clear();
        using (LogContext.PushProperty("k1", "v1"))
        {
            Assert.NotNull(LogContext.GetCurrentContext());
        }
        Assert.Null(LogContext.GetCurrentContext());
    }

    [Fact]
    public void AppVersionStaticsNonEmpty()
    {
        Assert.NotNull(AppVersion.Full);
        Assert.NotNull(AppVersion.Version);
        Assert.NotNull(AppVersion.Short);
    }
}
