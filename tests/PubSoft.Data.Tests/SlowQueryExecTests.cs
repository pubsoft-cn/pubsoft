using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SlowQueryExecTests
{
    private sealed class CaptureLogger : ILogger<SlowQueryInterceptor>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt)
            => Messages.Add(fmt(state, ex));
    }

    private sealed class Ctx(DbContextOptions<Ctx> o) : DbContext(o) { }

    private static (Ctx, CaptureLogger) Setup(TimeSpan threshold)
    {
        var log = new CaptureLogger();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<Ctx>()
            .UseSqlite(conn)
            .AddInterceptors(new SlowQueryInterceptor(threshold, log))
            .Options;
        return (new Ctx(opts), log);
    }

    [Fact]
    public async Task SlowQueryLogsWarningWhenOverThreshold()
    {
        var (ctx, log) = Setup(TimeSpan.FromTicks(-1)); // everything is "slow"
        await ctx.Database.ExecuteSqlRawAsync("SELECT 1");
        Assert.Contains(log.Messages, m => m.Contains("SLOW QUERY"));
    }

    [Fact]
    public void SyncPathBypassesInterceptor()
    {
        // interceptor only overrides *ExecutedAsync — sync ExecuteSqlRaw never logs (documented gap)
        var (ctx, log) = Setup(TimeSpan.FromTicks(-1));
        ctx.Database.ExecuteSqlRaw("SELECT 1");
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task FastQueryDoesNotLogWhenUnderThreshold()
    {
        var (ctx, log) = Setup(TimeSpan.FromMinutes(5));
        await ctx.Database.ExecuteSqlRawAsync("SELECT 1");
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task LongSqlIsTruncatedAt500Chars()
    {
        var (ctx, log) = Setup(TimeSpan.FromTicks(-1));
        var sql = "SELECT " + new string(' ', 600) + "1"; // valid SQL > 500 chars
        await ctx.Database.ExecuteSqlRawAsync(sql);
        Assert.Contains(log.Messages, m => m.Contains("...(truncated)"));
    }
}
