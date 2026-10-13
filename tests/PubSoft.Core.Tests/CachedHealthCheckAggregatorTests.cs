using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CachedHealthCheckAggregatorTests
{
    [Fact]
    public async Task ConcurrentRequestsShareRefreshAndReuseHealthyCache()
    {
        var check = new BlockingHealthCheck(AppHealthCheckResult.Healthy());
        var aggregator = new CachedHealthCheckAggregator([check]);

        var first = aggregator.CheckAllAsync();
        await check.Started.Task;
        var second = aggregator.CheckAllAsync();
        check.Release.TrySetResult();

        await Task.WhenAll(first, second);
        await aggregator.CheckAllAsync();

        Assert.Equal(1, check.CallCount);
    }

    [Fact]
    public async Task UnhealthyResultsUseShortTtlAndInvalidationForcesRefresh()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var check = new MutableHealthCheck(AppHealthCheckResult.Unhealthy("offline"));
        var aggregator = new CachedHealthCheckAggregator(
            [check],
            healthyCacheDuration: TimeSpan.FromMinutes(1),
            unhealthyCacheDuration: TimeSpan.FromSeconds(1),
            timeProvider: clock);

        Assert.Equal(AppHealthStatus.Unhealthy, await aggregator.GetOverallStatusAsync());
        check.Result = AppHealthCheckResult.Healthy();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(AppHealthStatus.Healthy, await aggregator.GetOverallStatusAsync());

        check.Result = AppHealthCheckResult.Unhealthy("offline again");
        aggregator.InvalidateCache();
        Assert.Equal(AppHealthStatus.Unhealthy, await aggregator.GetOverallStatusAsync());
        Assert.Equal(3, check.CallCount);
    }

    [Fact]
    public async Task InvalidationDuringRefreshPreventsStaleResultFromBeingCached()
    {
        var check = new BlockingHealthCheck(AppHealthCheckResult.Healthy());
        var aggregator = new CachedHealthCheckAggregator([check]);
        var refresh = aggregator.CheckAllAsync();
        await check.Started.Task;

        aggregator.InvalidateCache();
        check.Release.TrySetResult();
        await refresh;
        await aggregator.CheckAllAsync();

        Assert.Equal(2, check.CallCount);
    }

    [Fact]
    public void NonpositiveCacheDurationsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CachedHealthCheckAggregator(
            [], healthyCacheDuration: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CachedHealthCheckAggregator(
            [], unhealthyCacheDuration: TimeSpan.FromMilliseconds(-1)));
    }

    private sealed class BlockingHealthCheck(AppHealthCheckResult result) : IAppHealthCheck
    {
        private int _callCount;

        public string Name => "blocking";
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class MutableHealthCheck(AppHealthCheckResult result) : IAppHealthCheck
    {
        private int _callCount;

        public string Name => "mutable";
        public int CallCount => Volatile.Read(ref _callCount);
        public AppHealthCheckResult Result { get; set; } = result;

        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}