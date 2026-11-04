using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingTailWaveTests
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

    [Fact]
    public void ExecutorRegistryRegisterResolve()
    {
        var e = new ExecutorRegistry([]);
        Assert.Null(e.Resolve("http"));
        e.Register(new Exec("http"));
        Assert.NotNull(e.Resolve("http"));
        Assert.Null(e.Resolve("grpc"));
        // ctor with initial set
        var pre = new ExecutorRegistry([new Exec("sql")]);
        Assert.NotNull(pre.Resolve("sql"));
    }

    [Fact]
    public void ResultProcessorRegistryRequiresName()
    {
        var r = new ResultProcessorRegistry([]);
        Assert.Null(r.Resolve("log"));
        r.Register(new Proc("log"));
        Assert.NotNull(r.Resolve("log"));
        Assert.Throws<ArgumentException>(() => r.Register(new Proc("")));
    }

    [Fact]
    public async Task LoggingResultProcessorNameAndProcess()
    {
        var p = new LoggingResultProcessor();
        Assert.Equal("logging", p.Name);
        await p.ProcessAsync(new ScheduledJob { Name = "j" }, new JobResult { Success = true }, CancellationToken.None);
        await p.ProcessAsync(new ScheduledJob { Name = "j" }, new JobResult { Success = false, ErrorMessage = "x" }, CancellationToken.None); // failure path — logger null is fine
    }

    [Fact]
    public void ScheduleMaskExposesFields()
    {
        var m = ScheduleMasks.DailyAt(14, 30, 15);
        Assert.True(m.IsValid);
        Assert.NotEqual(0u, m.Hours);
        Assert.NotEqual(0u, m.Minutes);
        Assert.NotEqual(0u, m.Seconds);
        Assert.True(ScheduleMask.EveryHour.IsValid);
        Assert.True(ScheduleMask.EveryDay.IsValid);
        Assert.True(ScheduleMasks.EveryHours(3).IsValid);
    }

    [Fact]
    public void JobResultMetadataField()
    {
        var r = new JobResult { Success = true, Metadata = new Dictionary<string, object?> { ["k"] = "v" } };
        Assert.Equal("v", r.Metadata["k"]);
    }
}
