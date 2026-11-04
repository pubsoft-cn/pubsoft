using Microsoft.Extensions.DependencyInjection;
using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingTailWave2Tests
{
    private sealed class Exec(string type) : IJobExecutor
    {
        public string Type => type;
        public Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken ct) => Task.FromResult(new JobResult());
    }

    private sealed class Proc(string name) : IResultProcessor
    {
        public string Name => name;
        public Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class State { public int N { get; set; } }

    [Fact]
    public async Task MemoryStateStoreCustomStateRoundTrip()
    {
        var s = new MemoryStateStore();
        Assert.Null(await s.GetCustomStateAsync<State>("k", CancellationToken.None));
        await s.SetCustomStateAsync("k", new State { N = 5 }, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(5, (await s.GetCustomStateAsync<State>("k", CancellationToken.None))!.N);
        await s.RemoveCustomStateAsync("k", CancellationToken.None);
        Assert.Null(await s.GetCustomStateAsync<State>("k", CancellationToken.None));
    }

    [Fact]
    public async Task CustomStateRejectsBadArgs()
    {
        var s = new MemoryStateStore();
        await Assert.ThrowsAsync<ArgumentException>(() => s.SetCustomStateAsync(" ", new State(), TimeSpan.FromMinutes(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => s.SetCustomStateAsync("k", (State?)null!, TimeSpan.FromMinutes(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => s.SetCustomStateAsync("k", new State(), TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void AddPubSoftSchedulerRegistersCore()
    {
        var s = new ServiceCollection();
        s.AddPubSoftScheduler().AddExecutor<Exec>().AddResultProcessor<Proc>();
        Assert.Contains(s, d => d.ServiceType == typeof(IExecutorRegistry));
        Assert.Contains(s, d => d.ServiceType == typeof(IResultProcessorRegistry));
        Assert.Contains(s, d => d.ServiceType == typeof(IStateStore));
        Assert.Contains(s, d => d.ServiceType == typeof(SchedulerEngine));
    }

    [Fact]
    public void SchedulerHostedServiceIsHosted()
    {
        var s = new ServiceCollection();
        s.AddPubSoftScheduler();
        Assert.Contains(s, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }
}
