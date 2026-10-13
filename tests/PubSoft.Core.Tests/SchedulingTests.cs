using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PubSoft.Scheduling;
using System.Text.Json;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingTests
{
    [Fact]
    public void ScheduleMaskMatchesAllCalendarFieldsAndUsesTimestampOffset()
    {
        var schedule = ScheduleMasks.WeeklyAt(DayOfWeek.Wednesday, 12, 34, 56);
        var matching = new DateTimeOffset(2026, 9, 30, 12, 34, 56, TimeSpan.FromHours(2));

        Assert.True(schedule.Matches(matching));
        Assert.False(schedule.Matches(matching.AddSeconds(1)));
    }

    [Fact]
    public void NextRunFindsNextCalendarCandidateWithoutSecondBySecondSearch()
    {
        var schedule = ScheduleMasks.DailyAt(23, 59, 59);
        var from = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero), schedule.NextRun(from));
    }

    [Fact]
    public void NextRunRoundsFractionalInputUpToTheNextWholeSecond()
    {
        var schedule = ScheduleMasks.DailyAt(12, 34, 57);
        var from = new DateTimeOffset(2026, 9, 30, 12, 34, 56, 500, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 34, 57, TimeSpan.Zero), schedule.NextRun(from));
    }

    [Fact]
    public void ScheduleFactoriesRejectInvalidIntervalsAndTimes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleMasks.EverySeconds(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleMasks.EveryMinutes(61));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleMasks.DailyAt(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleMask(0, 1, 1, 2, 2, 1));
    }

    [Fact]
    public void ScheduleMaskRoundTripsThroughSystemTextJson()
    {
        var schedule = ScheduleMasks.WeeklyAt(DayOfWeek.Friday, 7, 8, 9);

        var restored = JsonSerializer.Deserialize<ScheduleMask>(JsonSerializer.Serialize(schedule));

        Assert.Equal(schedule, restored);
    }

    [Fact]
    public void NextRunReturnsNullWhenNoMatchOccursWithinSearchHorizon()
    {
        var schedule = new ScheduleMask(1, 1, 1, 1U << 29, 1 << 2, 0x7F);
        var from = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(schedule.NextRun(from));
        Assert.Null(ScheduleMask.EverySecond.NextRun(DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task EngineRunsDueEnabledJobsAndUpdatesProcessorAndState()
    {
        var executor = new TestExecutor("test", _ => Task.FromResult(new JobResult { Success = true, StatusCode = 204 }));
        var processor = new TestProcessor("audit");
        var state = new MemoryStateStore();
        var engine = CreateEngine([executor], [processor], state);
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var options = new SchedulerOptions
        {
            Jobs =
            [
                new ScheduledJob { Name = "due", ExecutorType = "test", Schedule = ScheduleMask.EverySecond, ResultProcessors = ["audit"] },
                new ScheduledJob { Name = "disabled", ExecutorType = "test", Schedule = ScheduleMask.EverySecond, Enabled = false },
                new ScheduledJob { Name = "not-due", ExecutorType = "test", Schedule = ScheduleMasks.DailyAt(12) }
            ]
        };

        await engine.RunDueJobsAsync(options, now);

        Assert.Equal(1, executor.ExecutionCount);
        Assert.Equal(1, processor.ProcessCount);
        Assert.True((await state.GetLastResultAsync("due", default))!.Success);
    }

    [Fact]
    public async Task EngineBoundsConcurrentExecutions()
    {
        var executor = new CountingExecutor();
        var engine = CreateEngine([executor], [], new MemoryStateStore());
        var options = new SchedulerOptions
        {
            MaxConcurrentJobs = 2,
            Jobs = Enumerable.Range(0, 8)
                .Select(index => new ScheduledJob { Name = $"job-{index}", ExecutorType = "count", Schedule = ScheduleMask.EverySecond })
                .ToList()
        };

        await engine.RunDueJobsAsync(options, DateTimeOffset.UtcNow);

        Assert.Equal(8, executor.ExecutionCount);
        Assert.Equal(2, executor.MaximumConcurrency);
    }

    [Fact]
    public async Task EngineConvertsExecutorFailureToSafeResultAndContinuesPipeline()
    {
        var executor = new TestExecutor("throws", _ => throw new InvalidOperationException("secret endpoint response"));
        var processor = new TestProcessor("after");
        var state = new MemoryStateStore();
        var engine = CreateEngine([executor], [processor], state);
        var options = new SchedulerOptions
        {
            Jobs = [new ScheduledJob { Name = "job", ExecutorType = "throws", Schedule = ScheduleMask.EverySecond, ResultProcessors = ["after"] }]
        };

        await engine.RunDueJobsAsync(options, DateTimeOffset.UtcNow);

        var result = await state.GetLastResultAsync("job", default);
        Assert.False(result!.Success);
        Assert.Equal("Job execution failed.", result.ErrorMessage);
        Assert.Equal(1, processor.ProcessCount);
    }

    [Fact]
    public async Task EnginePropagatesHostCancellation()
    {
        var executor = new TestExecutor("wait", async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new JobResult();
        });
        var engine = CreateEngine([executor], [], new MemoryStateStore());
        var options = new SchedulerOptions
        {
            Jobs = [new ScheduledJob { Name = "job", ExecutorType = "wait", Schedule = ScheduleMask.EverySecond }]
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.RunDueJobsAsync(options, DateTimeOffset.UtcNow, cancellation.Token));
    }

    [Fact]
    public async Task MemoryStateStoreExpiresCustomEntriesUsingTimeProvider()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-09-30T00:00:00Z"));
        var state = new MemoryStateStore(clock);
        await state.SetCustomStateAsync("key", new Box { Value = 5 }, TimeSpan.FromMinutes(1), default);

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(await state.GetCustomStateAsync<Box>("key", default));
    }

    [Fact]
    public async Task MemoryStateStoreRejectsBlankReadKeys()
    {
        var state = new MemoryStateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => state.GetLastResultAsync(" ", default));
        await Assert.ThrowsAsync<ArgumentException>(() => state.GetCustomStateAsync<Box>("\t", default));
    }

    [Fact]
    public void AddPubSoftSchedulerRegistersCoreServicesAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPubSoftScheduler(options => options.Jobs.Add(
            new ScheduledJob { Name = "valid", ExecutorType = "manual", Schedule = ScheduleMask.EverySecond }));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SchedulerEngine>(provider.GetRequiredService<SchedulerEngine>());
        Assert.Single(provider.GetServices<IHostedService>());
        Assert.Single(provider.GetRequiredService<IOptions<SchedulerOptions>>().Value.Jobs);
    }

    [Fact]
    public void AddPubSoftSchedulerRejectsDuplicateNames()
    {
        var services = new ServiceCollection();
        services.AddPubSoftScheduler(options =>
        {
            options.Jobs.Add(new ScheduledJob { Name = "duplicate", ExecutorType = "x", Schedule = ScheduleMask.EverySecond });
            options.Jobs.Add(new ScheduledJob { Name = "DUPLICATE", ExecutorType = "x", Schedule = ScheduleMask.EverySecond });
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SchedulerOptions>>().Value);
    }

    private static SchedulerEngine CreateEngine(
        IEnumerable<IJobExecutor> executors,
        IEnumerable<IResultProcessor> processors,
        IStateStore state)
        => new(new ExecutorRegistry(executors), new ResultProcessorRegistry(processors), state);

    private sealed class TestExecutor(string type, Func<CancellationToken, Task<JobResult>> execute) : IJobExecutor
    {
        public string Type => type;
        public int ExecutionCount { get; private set; }

        public Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return execute(cancellationToken);
        }
    }

    private sealed class TestProcessor(string name) : IResultProcessor
    {
        public string Name => name;
        public int ProcessCount { get; private set; }

        public Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingExecutor : IJobExecutor
    {
        private int _active;
        private int _executionCount;
        private int _maximumConcurrency;

        public string Type => "count";
        public int ExecutionCount => Volatile.Read(ref _executionCount);
        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _executionCount);
            int observed;
            while (active > (observed = Volatile.Read(ref _maximumConcurrency)))
            {
                if (Interlocked.CompareExchange(ref _maximumConcurrency, active, observed) == observed)
                    break;
            }
            await Task.Delay(10, cancellationToken);
            Interlocked.Decrement(ref _active);
            return new JobResult { Success = true };
        }
    }

    private sealed class Box { public int Value { get; init; } }

    private sealed class MutableTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _now = initialTime;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}