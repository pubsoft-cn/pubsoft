using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ApplicationHealthChecksTests
{
    [Fact]
    public async Task CheckAllPreservesRegistrationOrderAndOverallStatusPriority()
    {
        var checks = new IAppHealthCheck[]
        {
            new TestHealthCheck("database", AppHealthCheckResult.Degraded("slow")),
            new TestHealthCheck("queue", AppHealthCheckResult.Healthy()),
            new TestHealthCheck("storage", AppHealthCheckResult.Unhealthy("offline"))
        };
        var aggregator = new HealthCheckAggregator(checks);

        var results = await aggregator.CheckAllAsync();

        Assert.Equal(["database", "queue", "storage"], results.Select(result => result.Name));
        Assert.Equal(AppHealthStatus.Unhealthy, await aggregator.GetOverallStatusAsync());
    }

    [Fact]
    public async Task EmptyCheckSetIsHealthy()
    {
        var aggregator = new HealthCheckAggregator([]);

        Assert.Empty(await aggregator.CheckAllAsync());
        Assert.Equal(AppHealthStatus.Healthy, await aggregator.GetOverallStatusAsync());
    }

    [Fact]
    public async Task CheckFailureDoesNotStopFollowingChecksOrExposeExceptionText()
    {
        var aggregator = new HealthCheckAggregator([
            new ThrowingHealthCheck("database"),
            new TestHealthCheck("queue", AppHealthCheckResult.Healthy())
        ]);

        var results = await aggregator.CheckAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(AppHealthStatus.Unhealthy, results[0].Result.Status);
        Assert.Equal("Health check failed.", results[0].Result.Description);
        Assert.Equal(AppHealthStatus.Healthy, results[1].Result.Status);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var aggregator = new HealthCheckAggregator([new CancellingHealthCheck()]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => aggregator.CheckAllAsync(cancellation.Token));
    }

    [Fact]
    public async Task HealthCheckRegistrationResolvesAllRegisteredChecks()
    {
        var services = new ServiceCollection();
        services.AddPubSoftHealthCheck<RegisteredHealthCheck>();
        services.AddPubSoftHealthChecks();
        using var provider = services.BuildServiceProvider();

        var aggregator = provider.GetRequiredService<HealthCheckAggregator>();

        Assert.Equal(AppHealthStatus.Healthy, await aggregator.GetOverallStatusAsync());
    }

    private sealed class TestHealthCheck(string name, AppHealthCheckResult result) : IAppHealthCheck
    {
        public string Name { get; } = name;
        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(result);
    }

    private sealed class ThrowingHealthCheck(string name) : IAppHealthCheck
    {
        public string Name { get; } = name;
        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException<AppHealthCheckResult>(new InvalidOperationException("private connection detail"));
    }

    private sealed class CancellingHealthCheck : IAppHealthCheck
    {
        public string Name => "cancelled";
        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromCanceled<AppHealthCheckResult>(cancellationToken);
    }

    private sealed class RegisteredHealthCheck : IAppHealthCheck
    {
        public string Name => "registered";
        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AppHealthCheckResult.Healthy());
    }
}