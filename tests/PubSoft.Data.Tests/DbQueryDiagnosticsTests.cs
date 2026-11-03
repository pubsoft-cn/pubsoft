using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DbQueryDiagnosticsTests
{
    [Fact]
    public async Task SlowLogsAndSnapshotsDoNotExposeSqlOrExceptionContents()
    {
        var logger = new CapturingLogger<DbQueryDiagnostics>();
        var diagnostics = new DbQueryDiagnostics(
            new DbQueryDiagnosticsOptions { SlowQueryThreshold = TimeSpan.FromMilliseconds(1) },
            logger);

        await diagnostics.ExecuteAsync(
            "load_user",
            "SELECT * FROM Users WHERE Email = 'private@example.test'",
            async _ =>
            {
                await Task.Delay(20);
                return 1;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => diagnostics.ExecuteAsync<int>(
            "save_user",
            "UPDATE Users SET Token = 'secret-token'",
            _ => throw new InvalidOperationException("sensitive database detail")));

        var snapshot = diagnostics.GetSnapshot();
        Assert.Equal(2, snapshot.TotalQueries);
        Assert.Equal(1, snapshot.TotalErrors);
        Assert.Equal(1, snapshot.SlowQueryCount);
        Assert.DoesNotContain("private@example.test", logger.Messages);
        Assert.DoesNotContain("secret-token", logger.Messages);
        Assert.DoesNotContain("sensitive database detail", logger.Messages);
        Assert.All(snapshot.TopQueries, query => Assert.DoesNotContain("SELECT", query.QueryFingerprint));
    }

    [Fact]
    public async Task ConcurrentExecutionsAreCountedWithoutLostUpdates()
    {
        var diagnostics = new DbQueryDiagnostics(new DbQueryDiagnosticsOptions
        {
            SlowQueryThreshold = TimeSpan.Zero,
            MaxLatencySamplesPerTemplate = 16
        });

        await Parallel.ForEachAsync(Enumerable.Range(0, 2_000), async (_, cancellationToken) =>
        {
            await diagnostics.ExecuteAsync("read", "SELECT @id", async _ =>
            {
                await Task.Yield();
                return true;
            }, cancellationToken);
        });

        var snapshot = diagnostics.GetSnapshot();
        Assert.Equal(2_000, snapshot.TotalQueries);
        Assert.Equal(2_000, Assert.Single(snapshot.TopQueries).Count);
    }

    [Fact]
    public async Task QueryTemplateAndLatencyStorageAreBounded()
    {
        var diagnostics = new DbQueryDiagnostics(new DbQueryDiagnosticsOptions
        {
            SlowQueryThreshold = TimeSpan.Zero,
            MaxQueryTemplates = 2,
            MaxLatencySamplesPerTemplate = 4
        });

        for (var index = 0; index < 100; index++)
            await diagnostics.ExecuteAsync("read", $"SELECT {index}", _ => Task.FromResult(index));

        var snapshot = diagnostics.GetSnapshot();
        Assert.Equal(100, snapshot.TotalQueries);
        Assert.InRange(snapshot.TopQueries.Count, 1, 3);
        Assert.Equal(100, snapshot.TopQueries.Sum(query => query.Count));
    }

    [Fact]
    public void OptionsRejectInvalidSamplingAndCapacities()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { SampleRate = 1.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { MaxQueryTemplates = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { MaxLatencySamplesPerTemplate = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { MaxQueryTemplates = int.MaxValue }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { MaxLatencySamplesPerTemplate = int.MaxValue }));
    }

    [Fact]
    public async Task RequestedCancellationIsNotCountedAsDatabaseFailure()
    {
        var diagnostics = new DbQueryDiagnostics();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => diagnostics.ExecuteAsync<int>(
            "read",
            "SELECT 1",
            token => Task.FromCanceled<int>(token),
            cancellation.Token));

        Assert.Equal(0, diagnostics.GetSnapshot().TotalQueries);
        Assert.Equal(0, diagnostics.GetSnapshot().TotalErrors);
    }

    [Fact]
    public void DiagnosticsCanBeRegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddPubSoftDbQueryDiagnostics(options => options.SampleRate = 0.5);
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<DbQueryDiagnostics>(),
            provider.GetRequiredService<DbQueryDiagnostics>());
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> _messages = new();
        public string Messages => string.Join("\n", _messages);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _messages.Enqueue(formatter(state, exception));
    }
}