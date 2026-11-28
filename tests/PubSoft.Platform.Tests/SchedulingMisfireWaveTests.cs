using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.Caching;
using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingMisfireWaveTests
{
    [Fact]
    public async Task ExactRunPassesScheduledTimeAndFencingTokenToExecutor()
    {
        var executor = new CapturingExecutor();
        var state = new MemoryStateStore();
        var events = new List<string>();
        var engine = CreateEngine(executor, state, new FencingLock(events, fencingToken: 42));
        var scheduled = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);

        await engine.RunDueJobsAsync(new SchedulerOptions
        {
            DistributedLocking = true,
            Jobs = [Job(schedule: ScheduleMask.EveryMinute)]
        }, scheduled);

        var context = Assert.Single(executor.Contexts);
        Assert.Equal(scheduled, context.ScheduledTime);
        Assert.False(context.IsMisfireRecovery);
        Assert.Equal(42, context.FencingToken);
    }

    [Fact]
    public async Task DistributedLeaseStaysHeldUntilResultStateIsPersisted()
    {
        var executor = new CapturingExecutor();
        var events = new List<string>();
        var state = new RecordingStateStore(events);
        var engine = CreateEngine(executor, state, new FencingLock(events, fencingToken: 7));

        await engine.RunDueJobsAsync(new SchedulerOptions
        {
            DistributedLocking = true,
            Jobs = [Job(schedule: ScheduleMask.EveryMinute)]
        }, new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero));

        Assert.True(events.IndexOf("state") >= 0, $"missing state write: {string.Join(",", events)}");
        Assert.True(events.IndexOf("state") < events.IndexOf("release"),
            $"state must persist before lease release: {string.Join(",", events)}");
    }

    [Fact]
    public async Task MisfireCatchUpFiresLatestMissedOccurrenceOnlyOnce()
    {
        var executor = new CapturingExecutor();
        var engine = CreateEngine(executor, new MemoryStateStore());
        var options = MisfireOptions(ScheduleMasks.EveryMinutes(1), TimeSpan.FromMinutes(10));
        var baseline = new DateTimeOffset(2026, 11, 28, 0, 0, 30, TimeSpan.Zero);

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero));
        executor.Clear();
        await engine.RunDueJobsAsync(options, baseline);
        Assert.Empty(executor.Contexts);

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 2, 30, TimeSpan.Zero));

        var context = Assert.Single(executor.Contexts);
        Assert.True(context.IsMisfireRecovery);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 0, 2, 0, TimeSpan.Zero), context.ScheduledTime);

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 2, 31, TimeSpan.Zero));
        Assert.Single(executor.Contexts);
    }

    [Fact]
    public async Task MisfireBacklogOutsideWindowIsDiscardedWithoutCatchingUp()
    {
        var executor = new CapturingExecutor();
        var engine = CreateEngine(executor, new MemoryStateStore());
        var options = MisfireOptions(ScheduleMasks.EveryHours(1), TimeSpan.FromMinutes(45));

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero));
        executor.Clear();
        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 30, 0, TimeSpan.Zero));
        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 5, 46, 0, TimeSpan.Zero));
        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 5, 47, 0, TimeSpan.Zero));

        Assert.Empty(executor.Contexts);

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero));
        var context = Assert.Single(executor.Contexts);
        Assert.False(context.IsMisfireRecovery);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero), context.ScheduledTime);
    }

    [Fact]
    public async Task MisfireExactTickConsumesTheBacklogAsTheSingleCompressedRun()
    {
        var executor = new CapturingExecutor();
        var engine = CreateEngine(executor, new MemoryStateStore());
        var options = MisfireOptions(ScheduleMasks.EveryHours(1), TimeSpan.FromHours(2));

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero));
        executor.Clear();
        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 30, 0, TimeSpan.Zero));
        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero));
        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 3, 0, 1, TimeSpan.Zero));

        var context = Assert.Single(executor.Contexts);
        Assert.False(context.IsMisfireRecovery);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero), context.ScheduledTime);
    }

    [Fact]
    public async Task MisfireWatermarkReadFailureSkipsCatchUpInsteadOfGuessing()
    {
        var executor = new CapturingExecutor();
        var engine = CreateEngine(executor, new ThrowingCustomStateStore());
        var options = MisfireOptions(ScheduleMasks.EveryMinutes(1), TimeSpan.FromMinutes(10));

        await engine.RunDueJobsAsync(options, new DateTimeOffset(2026, 11, 28, 0, 2, 30, TimeSpan.Zero));

        Assert.Empty(executor.Contexts);
    }

    [Fact]
    public void LastRunAtOrBeforeFindsInclusiveCalendarCandidateAcrossMidnight()
    {
        var schedule = ScheduleMasks.DailyAt(2, 30, 0);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 28, 2, 30, 0, TimeSpan.Zero),
            schedule.LastRunAtOrBefore(new DateTimeOffset(2026, 11, 28, 2, 30, 0, TimeSpan.Zero)));
        Assert.Equal(
            new DateTimeOffset(2026, 11, 27, 2, 30, 0, TimeSpan.Zero),
            schedule.LastRunAtOrBefore(new DateTimeOffset(2026, 11, 28, 1, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task OptionsRejectInvalidMisfireAndLeaseValues()
    {
        var executor = new CapturingExecutor();
        var engine = CreateEngine(executor, new MemoryStateStore());

        await Assert.ThrowsAsync<OptionsValidationException>(() => engine.RunDueJobsAsync(new SchedulerOptions
        {
            MisfireWindow = TimeSpan.Zero,
            Jobs = [Job()]
        }, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<OptionsValidationException>(() => engine.RunDueJobsAsync(new SchedulerOptions
        {
            DistributedLocking = true,
            DistributedLockLease = TimeSpan.Zero,
            Jobs = [Job()]
        }, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<OptionsValidationException>(() => engine.RunDueJobsAsync(new SchedulerOptions
        {
            Jobs = [Job(misfirePolicy: (JobMisfirePolicy)99)]
        }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BindingReadsDistributedLeaseAndMisfireConfiguration()
    {
        var options = SchedulerConfigurationBinding.Bind(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedLocking"] = "true",
                ["DistributedLockLease"] = "10m",
                ["MisfireWindow"] = "15m",
                ["Jobs:0:Name"] = "job",
                ["Jobs:0:Schedule:EveryMinutes"] = "5",
                ["Jobs:0:MisfirePolicy"] = "FireOnce",
            }).Build());

        Assert.True(options.DistributedLocking);
        Assert.Equal(TimeSpan.FromMinutes(10), options.DistributedLockLease);
        Assert.Equal(TimeSpan.FromMinutes(15), options.MisfireWindow);
        Assert.Equal(JobMisfirePolicy.FireOnce, options.Jobs[0].MisfirePolicy);
    }

    [Fact]
    public void ConfigurationRegistrationCarriesDistributedLeaseAndMisfireOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedLocking"] = "true",
                ["DistributedLockLease"] = "10m",
                ["MisfireWindow"] = "15m",
                ["Jobs:0:Name"] = "job",
                ["Jobs:0:Schedule:EveryMinutes"] = "5",
                ["Jobs:0:MisfirePolicy"] = "FireOnce",
            }).Build();
        var services = new ServiceCollection();
        services.AddPubSoftScheduler(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SchedulerOptions>>().Value;

        Assert.True(options.DistributedLocking);
        Assert.Equal(TimeSpan.FromMinutes(10), options.DistributedLockLease);
        Assert.Equal(TimeSpan.FromMinutes(15), options.MisfireWindow);
        Assert.Equal(JobMisfirePolicy.FireOnce, options.Jobs[0].MisfirePolicy);
    }

    [Fact]
    public void BindingRejectsUnknownMisfirePolicyInsteadOfSilentlyIgnoringIt()
    {
        var exception = Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jobs:0:Name"] = "job",
                    ["Jobs:0:MisfirePolicy"] = "catch-up-please",
                }).Build()));

        Assert.Contains("MisfirePolicy", exception.Message);
        Assert.Contains("job", exception.Message);
    }

    private static ScheduledJob Job(ScheduleMask? schedule = null, JobMisfirePolicy misfirePolicy = JobMisfirePolicy.Ignore)
        => new()
        {
            Name = "job",
            ExecutorType = "test",
            Schedule = schedule ?? ScheduleMask.EverySecond,
            MisfirePolicy = misfirePolicy,
        };

    private static SchedulerOptions MisfireOptions(ScheduleMask schedule, TimeSpan window)
        => new()
        {
            MisfireWindow = window,
            Jobs = [Job(schedule, JobMisfirePolicy.FireOnce)]
        };

    private static SchedulerEngine CreateEngine(
        CapturingExecutor executor,
        IStateStore state,
        IDistributedLock? distributedLock = null)
        => new(
            new ExecutorRegistry([executor]),
            new ResultProcessorRegistry([]),
            state,
            distributedLock: distributedLock);

    private sealed class CapturingExecutor : IJobExecutor
    {
        private readonly object _gate = new();
        private readonly List<JobExecutionContext> _contexts = [];

        public string Type => "test";

        public IReadOnlyList<JobExecutionContext> Contexts
        {
            get
            {
                lock (_gate)
                    return _contexts.ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate)
                _contexts.Clear();
        }

        public Task<JobResult> ExecuteAsync(
            ScheduledJob job,
            JobExecutionContext executionContext,
            CancellationToken cancellationToken)
        {
            lock (_gate)
                _contexts.Add(executionContext);
            return Task.FromResult(new JobResult { Success = true });
        }
    }

    private sealed class RecordingStateStore(List<string> events) : IStateStore
    {
        private readonly MemoryStateStore _inner = new();

        public Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken)
            => _inner.GetLastResultAsync(jobName, cancellationToken);

        public Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken)
        {
            events.Add("state");
            return _inner.UpdateStateAsync(jobName, result, cancellationToken);
        }

        public Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class
            => _inner.GetCustomStateAsync<T>(key, cancellationToken);

        public Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken) where T : class
            => _inner.SetCustomStateAsync(key, value, expiry, cancellationToken);

        public Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken)
            => _inner.RemoveCustomStateAsync(key, cancellationToken);
    }

    private sealed class ThrowingCustomStateStore : IStateStore
    {
        private readonly MemoryStateStore _inner = new();

        public Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken)
            => _inner.GetLastResultAsync(jobName, cancellationToken);
        public Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken)
            => _inner.UpdateStateAsync(jobName, result, cancellationToken);
        public Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class
            => throw new InvalidOperationException("state store unavailable");
        public Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken) where T : class
            => _inner.SetCustomStateAsync(key, value, expiry, cancellationToken);
        public Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken)
            => _inner.RemoveCustomStateAsync(key, cancellationToken);
    }

    private sealed class FencingLock(List<string> events, long fencingToken) : IDistributedLock
    {
        public Task<IAsyncDisposable?> AcquireAsync(
            string key,
            TimeSpan expiry,
            TimeSpan? waitTimeout = null,
            CancellationToken cancellationToken = default)
        {
            events.Add("acquire");
            return Task.FromResult<IAsyncDisposable?>(new Handle(key, fencingToken, events));
        }

        private sealed class Handle(string key, long token, List<string> events) : IFencedLockHandle
        {
            public string Key => key;
            public long FencingToken => token;

            public Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public ValueTask DisposeAsync()
            {
                events.Add("release");
                return ValueTask.CompletedTask;
            }
        }
    }
}
