using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.Monitoring;
using PubSoft.Monitoring.Scheduling;
using PubSoft.Notifications;
using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MonitoringSchedulingTests
{
    [Fact]
    public async Task ProcessorTracksThreeStateTransitionsAndDoesNotExposeErrorText()
    {
        var channel = new TestChannel("email");
        var processor = CreateProcessor([channel], threshold: 2);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["email"]
        }, new AlertRule
        {
            Name = "recovered",
            Condition = AlertConditionType.StateRecovered,
            Channels = ["email"]
        });

        await processor.ProcessAsync(job, FailedResult(1, "private failure details"), default);
        Assert.Empty(channel.Messages);

        await processor.ProcessAsync(job, FailedResult(2, "private failure details"), default);
        var confirmed = Assert.Single(channel.Messages);
        Assert.Contains("Unhealthy", confirmed.Content);
        Assert.DoesNotContain("private failure details", confirmed.Content);

        await processor.ProcessAsync(job, SuccessfulResult(3), default);

        Assert.Equal(2, channel.Messages.Count);
        Assert.Contains("recovered", channel.Messages[1].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeOutageSuppressesRecoveryAndDoesNotLeaveSuppressionStuck()
    {
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var channel = new TestChannel("email");
        var processor = CreateProcessor([channel], probe, threshold: 1);
        var recoveryJob = CreateJob(new AlertRule
        {
            Name = "recovered",
            Condition = AlertConditionType.StateRecovered,
            Channels = ["email"]
        });

        await processor.ProcessAsync(recoveryJob, FailedResult(1), default);
        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(recoveryJob, FailedResult(2), default);
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(recoveryJob, SuccessfulResult(3), default);
        Assert.Empty(channel.Messages);

        var failureJob = CreateJob(new AlertRule
        {
            Name = "failure",
            Condition = AlertConditionType.EveryFailure,
            Channels = ["email"]
        });
        await processor.ProcessAsync(failureJob, FailedResult(4), default);
        Assert.Single(channel.Messages);
    }

    [Fact]
    public async Task ProcessorEvaluatesDegradedFirstFailureSlowResponseAndRecoveredRules()
    {
        var channel = new TestChannel("email");
        var processor = CreateProcessor([channel], threshold: 2);
        var job = CreateJob(
            new AlertRule { Name = "degraded", Condition = AlertConditionType.StateDegraded, Channels = ["email"] },
            new AlertRule { Name = "first", Condition = AlertConditionType.FirstFailure, Channels = ["email"] },
            new AlertRule { Name = "every", Condition = AlertConditionType.EveryFailure, Channels = ["email"] },
            new AlertRule
            {
                Name = "slow",
                Condition = AlertConditionType.SlowResponse,
                Channels = ["email"],
                DelayThresholdMs = 100
            },
            new AlertRule { Name = "recovered", Condition = AlertConditionType.Recovered, Channels = ["email"] });

        await processor.ProcessAsync(job, FailedResult(1), default);
        await processor.ProcessAsync(job, new JobResult
        {
            Success = true,
            StatusCode = 200,
            DelayMs = 250,
            ExecutedAt = DateTimeOffset.Parse("2026-10-01T00:00:03Z")
        }, default);

        Assert.Equal(5, channel.Messages.Count);
        Assert.Contains(channel.Messages, message => message.Title!.Contains("degraded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(channel.Messages, message => message.Title!.Contains("first failure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(channel.Messages, message => message.Title!.Contains("slow response", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(channel.Messages, message => message.Title!.Contains("recovered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ThrottleStartsOnlyAfterSuccessfulDeliveryAndExpiresWithTimeProvider()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var channel = new TestChannel("email") { Result = NotificationResult.Fail("offline") };
        var processor = CreateProcessor([channel], timeProvider: clock);
        var job = CreateJob(new AlertRule
        {
            Name = "every-failure",
            Condition = AlertConditionType.EveryFailure,
            Channels = ["email"],
            Throttle = TimeSpan.FromMinutes(10)
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        channel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, FailedResult(2), default);
        await processor.ProcessAsync(job, FailedResult(3), default);
        Assert.Equal(2, channel.SendCount);

        clock.Advance(TimeSpan.FromMinutes(11));
        await processor.ProcessAsync(job, FailedResult(4), default);
        Assert.Equal(3, channel.SendCount);
    }

    [Fact]
    public async Task ProcessorPropagatesCallerCancellation()
    {
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var processor = CreateProcessor([], probe);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(CreateJob(), SuccessfulResult(1), cancellation.Token));
    }

    [Fact]
    public async Task ConcurrentCallsForOneJobAreSerializedWithinTheProcess()
    {
        var probe = new DelayedProbe();
        var processor = CreateProcessor([], probe);
        var job = CreateJob();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => processor.ProcessAsync(job, SuccessfulResult(index + 1), default)));

        Assert.Equal(1, probe.MaximumConcurrency);
    }

    [Fact]
    public void RegistrationAddsOneNamedProcessorAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IStateStore, MemoryStateStore>();
        services.AddSingleton<IProbeHealthCheck>(new TestProbe());
        services.AddPubSoftNotifications();
        services.AddPubSoftMonitoringScheduling(options => options.FailureThreshold = 2);
        services.AddPubSoftMonitoringScheduling();
        using var provider = services.BuildServiceProvider();

        var processor = Assert.IsType<AlertRuleProcessor>(
            Assert.Single(provider.GetServices<IResultProcessor>()));
        Assert.Equal("AlertRules", processor.Name);
    }

    private static AlertRuleProcessor CreateProcessor(
        IEnumerable<INotificationChannel> channels,
        IProbeHealthCheck? probe = null,
        int threshold = 3,
        TimeProvider? timeProvider = null)
        => new(
            new MemoryStateStore(timeProvider),
            new NotificationDispatcher(channels, timeProvider: timeProvider),
            probe ?? new TestProbe(),
            Options.Create(new AlertProcessorOptions { FailureThreshold = threshold }),
            timeProvider);

    private static ScheduledJob CreateJob(params AlertRule[] rules)
        => new()
        {
            Name = "test-job",
            ResultProcessorConfig = JsonSerializer.SerializeToElement(new AlertRuleConfig { Rules = [.. rules] })
        };

    private static JobResult FailedResult(int second, string? error = null)
        => new()
        {
            Success = false,
            StatusCode = 503,
            ErrorMessage = error,
            ExecutedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z").AddSeconds(second)
        };

    private static JobResult SuccessfulResult(int second)
        => new()
        {
            Success = true,
            StatusCode = 200,
            ExecutedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z").AddSeconds(second)
        };

    private sealed class TestProbe : IProbeHealthCheck
    {
        public ProbeHealth Health { get; set; } = ProbeHealth.Healthy;

        public Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Health);
        }
    }

    private sealed class DelayedProbe : IProbeHealthCheck
    {
        private int _active;
        private int _maximumConcurrency;

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            while (active > (observed = Volatile.Read(ref _maximumConcurrency)))
            {
                if (Interlocked.CompareExchange(ref _maximumConcurrency, active, observed) == observed)
                    break;
            }

            try
            {
                await Task.Delay(10, cancellationToken);
                return ProbeHealth.Healthy;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class TestChannel(string name) : INotificationChannel
    {
        public string Name { get; } = name;
        public List<NotificationMessage> Messages { get; } = [];
        public NotificationResult Result { get; set; } = NotificationResult.Ok();
        public int SendCount { get; private set; }

        public Task<NotificationResult> SendAsync(
            NotificationMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendCount++;
            Messages.Add(message);
            return Task.FromResult(Result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}