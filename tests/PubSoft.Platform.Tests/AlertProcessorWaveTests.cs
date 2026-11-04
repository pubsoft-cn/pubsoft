using Microsoft.Extensions.Options;
using PubSoft.Monitoring;
using PubSoft.Monitoring.Scheduling;
using PubSoft.Notifications;
using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AlertProcessorWaveTests
{
    [Fact]
    public void AlertRuleProcessorNameIsAlertRules()
    {
        var p = new AlertRuleProcessor(
            new FakeStore(), new NotificationDispatcher([]), new FakeProbe(),
            Options.Create(new AlertProcessorOptions()));
        Assert.Equal("AlertRules", p.Name);
    }

    [Fact]
    public void CtorRejectsNullDependencies()
    {
        var d = new NotificationDispatcher([]);
        var o = Options.Create(new AlertProcessorOptions());
        Assert.Throws<ArgumentNullException>(() => new AlertRuleProcessor(null!, d, new FakeProbe(), o));
        Assert.Throws<ArgumentNullException>(() => new AlertRuleProcessor(new FakeStore(), null!, new FakeProbe(), o));
    }

    private sealed class FakeStore : IStateStore
    {
        public Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken ct) => Task.FromResult<JobResult?>(null);
        public Task UpdateStateAsync(string jobName, JobResult result, CancellationToken ct) => Task.CompletedTask;
        public Task<T?> GetCustomStateAsync<T>(string key, CancellationToken ct) where T : class => Task.FromResult<T?>(null);
        public Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct) where T : class => Task.CompletedTask;
        public Task RemoveCustomStateAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeProbe : IProbeHealthCheck
    {
        public Task<ProbeHealth> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(ProbeHealth.Healthy);
    }
}
