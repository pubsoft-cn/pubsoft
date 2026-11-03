using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ParallelHealthCheckAggregatorTests
{
    [Fact]
    public async Task ChecksRunConcurrentlyWithinConfiguredLimitAndPreserveRegistrationOrder()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var checks = Enumerable.Range(0, 4)
            .Select(index => (IAppHealthCheck)new ConcurrentHealthCheck(
                $"check-{index}", release.Task, () =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    if (current == 2)
                        started.TrySetResult();
                }, () => Interlocked.Decrement(ref active)))
            .ToArray();
        var aggregator = new ParallelHealthCheckAggregator(checks, maxConcurrency: 2);

        var checking = aggregator.CheckAllAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref maximumActive));
        release.TrySetResult();
        var results = await checking;

        Assert.Equal(checks.Select(check => check.Name), results.Select(result => result.Name));
        Assert.Equal(2, maximumActive);
        Assert.All(results, result => Assert.Equal(AppHealthStatus.Healthy, result.Result.Status));
    }

    [Fact]
    public async Task PerCheckTimeoutReturnsUnhealthyAndContinuesToNextCheck()
    {
        var slow = new CooperativeSlowHealthCheck();
        var aggregator = new ParallelHealthCheckAggregator(
            [slow, new ImmediateHealthCheck("next")],
            timeout: TimeSpan.FromMilliseconds(30),
            maxConcurrency: 2);

        var results = await aggregator.CheckAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(AppHealthStatus.Unhealthy, results[0].Result.Status);
        Assert.Equal("Health check timed out.", results[0].Result.Description);
        await slow.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(slow.ObservedCancellation);
        Assert.Equal(AppHealthStatus.Healthy, results[1].Result.Status);
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBecomingTimeoutResult()
    {
        var aggregator = new ParallelHealthCheckAggregator(
            [new CooperativeSlowHealthCheck()],
            timeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => aggregator.CheckAllAsync(cancellation.Token));
    }

    [Fact]
    public void InvalidTimeoutAndConcurrencyAreRejected()
    {
        var check = new ImmediateHealthCheck("check");

        Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelHealthCheckAggregator(
            [check], timeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelHealthCheckAggregator(
            [check], maxConcurrency: 0));
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var current = Volatile.Read(ref maximum);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private sealed class ConcurrentHealthCheck(
        string name,
        Task release,
        Action onStarted,
        Action onCompleted) : IAppHealthCheck
    {
        public string Name { get; } = name;

        public async ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            onStarted();
            try
            {
                await release.WaitAsync(cancellationToken);
                return AppHealthCheckResult.Healthy();
            }
            finally
            {
                onCompleted();
            }
        }
    }

    private sealed class CooperativeSlowHealthCheck : IAppHealthCheck
    {
        private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "slow";
        public bool ObservedCancellation { get; private set; }
        public Task CancellationObserved => _cancellationObserved.Task;

        public async ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return AppHealthCheckResult.Healthy();
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                _cancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private sealed class ImmediateHealthCheck(string name) : IAppHealthCheck
    {
        public string Name { get; } = name;
        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AppHealthCheckResult.Healthy());
    }
}
