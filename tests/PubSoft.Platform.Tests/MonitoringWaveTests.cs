using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MonitoringWaveTests
{
    private sealed class StubCheck(string name, AppHealthCheckResult result) : IAppHealthCheck
    {
        public string Name => name;
        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken ct = default)
            => ValueTask.FromResult(result);
    }

    [Fact]
    public void ResultFactories()
    {
        Assert.Equal(AppHealthStatus.Healthy, AppHealthCheckResult.Healthy().Status);
        Assert.Equal(AppHealthStatus.Degraded, AppHealthCheckResult.Degraded("d").Status);
        Assert.Equal(AppHealthStatus.Unhealthy, AppHealthCheckResult.Unhealthy("u").Status);
        Assert.Equal("d", AppHealthCheckResult.Degraded("d").Description);
    }

    [Fact]
    public async Task AggregatorWorstWins()
    {
        var agg = new HealthCheckAggregator([
            new StubCheck("a", AppHealthCheckResult.Healthy()),
            new StubCheck("b", AppHealthCheckResult.Degraded("warn")),
        ]);
        Assert.Equal(AppHealthStatus.Degraded, await agg.GetOverallStatusAsync());

        var agg2 = new HealthCheckAggregator([
            new StubCheck("a", AppHealthCheckResult.Healthy()),
            new StubCheck("b", AppHealthCheckResult.Unhealthy("down")),
        ]);
        Assert.Equal(AppHealthStatus.Unhealthy, await agg2.GetOverallStatusAsync());
    }

    [Fact]
    public async Task AggregatorAllHealthy()
    {
        var agg = new HealthCheckAggregator([
            new StubCheck("a", AppHealthCheckResult.Healthy()),
            new StubCheck("b", AppHealthCheckResult.Healthy()),
        ]);
        Assert.Equal(AppHealthStatus.Healthy, await agg.GetOverallStatusAsync());
        var all = await agg.CheckAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("a", all[0].Name);
    }

    [Fact]
    public void AggregatorRejectsNullCheck()
        => Assert.Throws<ArgumentException>(() => new HealthCheckAggregator([null!]));
}
