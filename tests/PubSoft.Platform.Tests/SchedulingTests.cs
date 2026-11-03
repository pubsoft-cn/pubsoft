using Microsoft.Extensions.Configuration;
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
    public async Task EngineRetriesRetryableFailureUntilSuccess()
    {
        var attempts = 0;
        var executor = new TestExecutor("flaky", _ =>
        {
            attempts++;
            return Task.FromResult(attempts < 3
                ? new JobResult { Success = false, ErrorMessage = "transient", Retryable = true }
                : new JobResult { Success = true });
        });
        var state = new MemoryStateStore();
        var engine = CreateEngine([executor], [], state);
        var options = new SchedulerOptions
        {
            Jobs =
            [
                new ScheduledJob
                {
                    Name = "job", ExecutorType = "flaky", Schedule = ScheduleMask.EverySecond,
                    Retry = new JobRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) }
                }
            ]
        };

        await engine.RunDueJobsAsync(options, DateTimeOffset.UtcNow);

        Assert.Equal(3, executor.ExecutionCount);
        Assert.True((await state.GetLastResultAsync("job", default))!.Success);
    }

    [Fact]
    public async Task EngineDoesNotRetryNonRetryableFailure()
    {
        var executor = new TestExecutor("hard", _ =>
            Task.FromResult(new JobResult { Success = false, ErrorMessage = "permanent" }));
        var state = new MemoryStateStore();
        var engine = CreateEngine([executor], [], state);
        var options = new SchedulerOptions
        {
            Jobs =
            [
                new ScheduledJob
                {
                    Name = "job", ExecutorType = "hard", Schedule = ScheduleMask.EverySecond,
                    Retry = new JobRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) }
                }
            ]
        };

        await engine.RunDueJobsAsync(options, DateTimeOffset.UtcNow);

        Assert.Equal(1, executor.ExecutionCount);
        Assert.False((await state.GetLastResultAsync("job", default))!.Success);
    }

    [Fact]
    public async Task EngineSkipsDueRunWhilePreviousRunIsExecuting()
    {
        var gate = new TaskCompletionSource();
        var executor = new TestExecutor("slow", async token =>
        {
            await gate.Task;
            return new JobResult { Success = true };
        });
        var engine = CreateEngine([executor], [], new MemoryStateStore());
        var options = new SchedulerOptions
        {
            Jobs = [new ScheduledJob { Name = "job", ExecutorType = "slow", Schedule = ScheduleMask.EverySecond }]
        };
        var now = DateTimeOffset.UtcNow;

        var first = engine.RunDueJobsAsync(options, now);
        await Task.Delay(30); // let the first run acquire the job lock
        await engine.RunDueJobsAsync(options, now.AddSeconds(1)); // skipped: overlap
        Assert.Equal(1, executor.ExecutionCount);

        gate.SetResult();
        await first;
    }

    [Fact]
    public async Task EngineConvertsJobTimeoutToFailedResult()
    {
        var executor = new TestExecutor("hang", async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new JobResult { Success = true };
        });
        var state = new MemoryStateStore();
        var engine = CreateEngine([executor], [], state);
        var options = new SchedulerOptions
        {
            Jobs =
            [
                new ScheduledJob
                {
                    Name = "job", ExecutorType = "hang", Schedule = ScheduleMask.EverySecond,
                    Timeout = TimeSpan.FromMilliseconds(30)
                }
            ]
        };

        await engine.RunDueJobsAsync(options, DateTimeOffset.UtcNow);

        var result = await state.GetLastResultAsync("job", default);
        Assert.False(result!.Success);
        Assert.Equal(408, result.StatusCode);
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

    [Fact]
    public void AddPubSoftSchedulerBindsJobsFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:0:Name"] = "probe",
            ["Jobs:0:ExecutorType"] = "Http.Probe",
            ["Jobs:0:Schedule:Seconds"] = "1152921504606846975",
            ["Jobs:0:Schedule:Minutes"] = "1152921504606846975",
            ["Jobs:0:Schedule:Hours"] = "16777215",
            ["Jobs:0:Schedule:DaysOfMonth"] = "4294967294",
            ["Jobs:0:Schedule:Months"] = "8190",
            ["Jobs:0:Schedule:DaysOfWeek"] = "127",
            ["Jobs:0:ExecutorConfig:Method"] = "POST",
            ["Jobs:0:ExecutorConfig:Url"] = "https://service.test/probe",
            ["Jobs:0:ExecutorConfig:ExpectStatusCodes:0"] = "200",
            ["Jobs:0:ExecutorConfig:Timeout"] = "00:00:08",
            ["Jobs:0:ResultProcessors:0"] = "AlertRules",
            ["Jobs:0:ResultProcessors:1"] = "logging",
            ["Jobs:0:ResultProcessorConfig:Rules:0:Name"] = "slow",
            ["Jobs:0:ResultProcessorConfig:Rules:0:DelayThresholdMs"] = "2000",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPubSoftScheduler(config);
        using var provider = services.BuildServiceProvider();

        var job = Assert.Single(provider.GetRequiredService<IOptions<SchedulerOptions>>().Value.Jobs);
        Assert.Equal("probe", job.Name);
        Assert.True(job.Enabled);
        Assert.Equal("Http.Probe", job.ExecutorType);
        Assert.True(job.Schedule.IsValid);
        Assert.Equal(["AlertRules", "logging"], job.ResultProcessors);
        Assert.Equal(JsonValueKind.Object, job.ExecutorConfig.ValueKind);
        Assert.Equal("https://service.test/probe", job.ExecutorConfig.GetProperty("Url").GetString());
        Assert.Equal(200, job.ExecutorConfig.GetProperty("ExpectStatusCodes")[0].GetInt32());
        Assert.Equal("00:00:08", job.ExecutorConfig.GetProperty("Timeout").GetString());
        Assert.Equal("slow", job.ResultProcessorConfig.GetProperty("Rules")[0].GetProperty("Name").GetString());
    }

    // ── strict scalar binding ──

    private static IConfiguration JobConfig(Dictionary<string, string?> values)
    {
        var data = new Dictionary<string, string?>
        {
            ["Jobs:0:Name"] = "job",
            ["Jobs:0:Schedule:Seconds"] = "1152921504606846975",
            ["Jobs:0:Schedule:Minutes"] = "1152921504606846975",
            ["Jobs:0:Schedule:Hours"] = "16777215",
            ["Jobs:0:Schedule:DaysOfMonth"] = "4294967294",
            ["Jobs:0:Schedule:Months"] = "8190",
            ["Jobs:0:Schedule:DaysOfWeek"] = "127",
        };
        foreach (var (k, v) in values) data[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Theory]
    [InlineData("5m", 300)]
    [InlineData("30s", 30)]
    [InlineData("1h", 3600)]
    [InlineData("500ms", 0.5)]
    [InlineData("2d", 172800)]
    [InlineData("00:00:30", 30)]
    public void BindingParsesDurationSuffixesForJobTimeout(string raw, double seconds)
    {
        var job = SchedulerConfigurationBinding.Bind(
            JobConfig(new() { ["Jobs:0:Timeout"] = raw })).Jobs.Single();
        Assert.Equal(TimeSpan.FromSeconds(seconds), job.Timeout);
    }

    [Fact]
    public void BindingRejectsUnparsableTimeoutInsteadOfSilentUnbounded()
    {
        var exception = Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(
            JobConfig(new() { ["Jobs:0:Timeout"] = "soon" })));
        Assert.Contains("job", exception.Message);
        Assert.Contains("Timeout", exception.Message);
    }

    [Fact]
    public void BindingParsesPreventOverlapAndRetrySection()
    {
        var job = SchedulerConfigurationBinding.Bind(JobConfig(new()
        {
            ["Jobs:0:PreventOverlap"] = "false",
            ["Jobs:0:Retry:MaxAttempts"] = "3",
            ["Jobs:0:Retry:InitialDelay"] = "2s",
            ["Jobs:0:Retry:BackoffFactor"] = "1.5",
            ["Jobs:0:Retry:MaxDelay"] = "30s",
        })).Jobs.Single();

        Assert.False(job.PreventOverlap);
        Assert.Equal(3, job.Retry!.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), job.Retry.InitialDelay);
        Assert.Equal(1.5, job.Retry.BackoffFactor);
        Assert.Equal(TimeSpan.FromSeconds(30), job.Retry.MaxDelay);
    }

    [Fact]
    public void BindingRejectsUnparsableRetryFieldWithJobContext()
    {
        var exception = Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(
            JobConfig(new() { ["Jobs:0:Retry:InitialDelay"] = "fast" })));
        Assert.Contains("'job'", exception.Message);
        Assert.Contains("Retry:InitialDelay", exception.Message);
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