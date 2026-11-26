using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Monitoring;
using PubSoft.Monitoring.Scheduling;
using PubSoft.Notifications;
using PubSoft.Scheduling;
using PubSoft.Scheduling.EntityFrameworkCore;
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
    public async Task EnvironmentWatchdogAlertsOnRecoveryAfterQualifyingOutage()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options => options.Environment = new EnvironmentAlertOptions
            {
                Enabled = true,
                MinUnreachableDuration = TimeSpan.FromMinutes(2),
                Channels = ["envops"],
                Severity = AlertSeverity.Critical
            });
        var job = CreateJob();

        await processor.ProcessAsync(job, FailedResult(1), default);
        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(job, FailedResult(2), default);
        await processor.ProcessAsync(job, FailedResult(3), default);
        clock.Advance(TimeSpan.FromMinutes(3));
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(job, SuccessfulResult(4), default);

        var message = Assert.Single(envChannel.Messages);
        Assert.Equal(NotificationSeverity.Critical, message.Severity);
        Assert.Equal("monitoring-env", message.Source);
        Assert.Contains("egress outage", message.Content);
        Assert.Equal("2", message.TemplateParameters["suppressedCount"]);
        Assert.Equal("3", message.TemplateParameters["windowMinutes"]);
    }

    [Fact]
    public async Task EnvironmentWatchdogSkipsSummaryBelowMinDuration()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Unreachable };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options => options.Environment = new EnvironmentAlertOptions
            {
                Enabled = true,
                MinUnreachableDuration = TimeSpan.FromMinutes(2),
                Channels = ["envops"]
            });
        var job = CreateJob();

        await processor.ProcessAsync(job, FailedResult(1), default);
        clock.Advance(TimeSpan.FromMinutes(1));
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Empty(envChannel.Messages);
    }

    [Fact]
    public async Task EnvironmentWatchdogReportsOnceAcrossMultipleJobs()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options => options.Environment = new EnvironmentAlertOptions
            {
                Enabled = true,
                MinUnreachableDuration = TimeSpan.FromMinutes(1),
                Channels = ["envops"]
            });
        var jobA = CreateJob();
        var jobB = CreateJob();
        jobB.Name = "job-b";

        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(jobA, FailedResult(1), default);
        await processor.ProcessAsync(jobB, FailedResult(2), default);
        clock.Advance(TimeSpan.FromMinutes(5));
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(jobA, SuccessfulResult(3), default);
        await processor.ProcessAsync(jobB, SuccessfulResult(4), default);

        var message = Assert.Single(envChannel.Messages);
        Assert.Equal("2", message.TemplateParameters["suppressedCount"]);
    }

    [Fact]
    public async Task EnvironmentWatchdogImmediateAlertFiresOncePerOutage()
    {
        var probe = new TestProbe { Health = ProbeHealth.Unreachable };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1,
            configure: options => options.Environment = new EnvironmentAlertOptions
            {
                Enabled = true,
                MinUnreachableDuration = TimeSpan.FromMinutes(2),
                Channels = ["envops"],
                SendImmediate = true
            });
        var job = CreateJob();

        await processor.ProcessAsync(job, FailedResult(1), default);
        await processor.ProcessAsync(job, FailedResult(2), default);

        var message = Assert.Single(envChannel.Messages);
        Assert.Contains("unreachable since", message.Content);
    }

    [Fact]
    public void EnvironmentWatchdogEnabledWithoutChannelsFailsValidation()
        => Assert.Throws<OptionsValidationException>(() => CreateProcessor([new TestChannel("envops")],
            configure: options => options.Environment = new EnvironmentAlertOptions { Enabled = true }));

    [Fact]
    public async Task EnvironmentWatchdogDisabledByDefaultStaysSilent()
    {
        var probe = new TestProbe { Health = ProbeHealth.Unreachable };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1);
        var job = CreateJob();

        await processor.ProcessAsync(job, FailedResult(1), default);
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Empty(envChannel.Messages);
    }

    [Fact]
    public async Task EnvironmentSummaryFailureIsResentWhenPendingEnabled()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options =>
            {
                options.Environment = new EnvironmentAlertOptions
                {
                    Enabled = true,
                    MinUnreachableDuration = TimeSpan.FromMinutes(1),
                    Channels = ["envops"]
                };
                options.PendingAlerts.Enabled = true;
            });
        var job = CreateJob();

        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(job, FailedResult(1), default);
        clock.Advance(TimeSpan.FromMinutes(3));
        probe.Health = ProbeHealth.Healthy;
        envChannel.Result = NotificationResult.Fail("envops down");
        await processor.ProcessAsync(job, SuccessfulResult(2), default);
        Assert.Equal(1, envChannel.SendCount);

        envChannel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(3), default);

        Assert.Equal(2, envChannel.SendCount);
        Assert.Equal(envChannel.Messages[0].CorrelationId, envChannel.Messages[1].CorrelationId);
    }

    [Fact]
    public async Task EnvironmentImmediateSuccessDoesNotSuppressRecoverySummary()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options => options.Environment = new EnvironmentAlertOptions
            {
                Enabled = true,
                MinUnreachableDuration = TimeSpan.FromMinutes(1),
                Channels = ["envops"],
                SendImmediate = true
            });
        var job = CreateJob();

        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(job, FailedResult(1), default);
        clock.Advance(TimeSpan.FromMinutes(2));
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Equal(2, envChannel.Messages.Count);
        Assert.NotEqual(envChannel.Messages[0].CorrelationId, envChannel.Messages[1].CorrelationId);
        Assert.Contains("unreachable since", envChannel.Messages[0].Content);
        Assert.Contains("egress outage", envChannel.Messages[1].Content);
    }

    [Fact]
    public async Task EnvironmentImmediateFailureIsNotResent()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var envChannel = new TestChannel("envops") { Result = NotificationResult.Fail("down") };
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options =>
            {
                options.Environment = new EnvironmentAlertOptions
                {
                    Enabled = true,
                    MinUnreachableDuration = TimeSpan.FromMinutes(1),
                    Channels = ["envops"],
                    SendImmediate = true
                };
                options.PendingAlerts.Enabled = true;
            });
        var job = CreateJob();

        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(job, FailedResult(1), default);
        clock.Advance(TimeSpan.FromMinutes(2));
        probe.Health = ProbeHealth.Healthy;
        envChannel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(2), default);
        await processor.ProcessAsync(job, SuccessfulResult(3), default);

        Assert.Single(envChannel.Messages, message => message.Content.Contains("unreachable since"));
        Assert.Single(envChannel.Messages, message => message.Content.Contains("egress outage"));
    }

    [Fact]
    public async Task EnvironmentPendingFlushIsThrottledAcrossJobExecutions()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Healthy };
        var envChannel = new TestChannel("envops") { Result = NotificationResult.Fail("envops down") };
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options =>
            {
                options.Environment = new EnvironmentAlertOptions
                {
                    Enabled = true,
                    MinUnreachableDuration = TimeSpan.FromMinutes(1),
                    Channels = ["envops"]
                };
                options.PendingAlerts.Enabled = true;
            });
        var jobA = CreateJob();
        jobA.Name = "job-a";
        var jobB = CreateJob();
        jobB.Name = "job-b";

        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(jobA, FailedResult(1), default);
        clock.Advance(TimeSpan.FromMinutes(3));
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(jobA, SuccessfulResult(2), default);
        Assert.Equal(1, envChannel.SendCount);

        // the first flush pass retries the failed summary once
        await processor.ProcessAsync(jobA, SuccessfulResult(3), default);
        Assert.Equal(2, envChannel.SendCount);

        // another job executing within the retry interval must not multiply the retry
        await processor.ProcessAsync(jobB, SuccessfulResult(4), default);
        await processor.ProcessAsync(jobA, SuccessfulResult(5), default);
        Assert.Equal(2, envChannel.SendCount);

        // once the retry interval elapses the next execution retries again
        clock.Advance(TimeSpan.FromMinutes(2));
        await processor.ProcessAsync(jobB, SuccessfulResult(6), default);
        Assert.Equal(3, envChannel.SendCount);
    }

    [Fact]
    public async Task ConcurrentOutageOnsetAcrossJobsFiresSingleImmediate()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var probe = new TestProbe { Health = ProbeHealth.Unreachable };
        var envChannel = new TestChannel("envops");
        var processor = CreateProcessor([envChannel], probe, threshold: 1, timeProvider: clock,
            configure: options => options.Environment = new EnvironmentAlertOptions
            {
                Enabled = true,
                MinUnreachableDuration = TimeSpan.FromMinutes(1),
                Channels = ["envops"],
                SendImmediate = true
            });

        var jobs = Enumerable.Range(1, 8).Select(index =>
        {
            var job = CreateJob();
            job.Name = $"test-job-{index}";
            return job;
        }).ToArray();
        await Task.WhenAll(jobs.Select(job => processor.ProcessAsync(job, FailedResult(1), default)));

        Assert.Single(envChannel.Messages, message => message.Content.Contains("unreachable since"));
    }

    [Fact]
    public async Task InhibitedRecoveryAtProbeRestorationIsConsumedNotReplayed()
    {
        var logger = new ListLogger<AlertRuleProcessor>();
        var channel = new TestChannel("email");
        var probe = new TestProbe();
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(),
            new NotificationDispatcher([channel]),
            probe,
            Options.Create(new AlertProcessorOptions { FailureThreshold = 1 }),
            logger: logger);
        var job = CreateJob(new AlertRule
        {
            Name = "recovered",
            Condition = AlertConditionType.StateRecovered,
            Channels = ["email"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        probe.Health = ProbeHealth.Unreachable;
        await processor.ProcessAsync(job, FailedResult(2), default);
        probe.Health = ProbeHealth.Healthy;
        await processor.ProcessAsync(job, SuccessfulResult(3), default);
        await processor.ProcessAsync(job, SuccessfulResult(4), default);

        Assert.Empty(channel.Messages);
        Assert.Contains(logger.Messages, message => message.Contains("inhibited"));
    }

    [Fact]
    public async Task PendingAlertsSurviveEfCoreStateStoreSerialization()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddSingleton<PubSoft.Domain.ITenantContext>(new StubTenantContext(null));
        services.AddDbContext<FakeDbContext>(options => options.UseSqlite(connection));
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<FakeDbContext>().Database.EnsureCreated();
        var store = new PubSoft.Scheduling.EntityFrameworkCore.EfCoreStateStore<FakeDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>());

        var channel = new TestChannel("email") { Result = NotificationResult.Fail("down") };
        var processor = new AlertRuleProcessor(
            store,
            new NotificationDispatcher([channel]),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions
            {
                FailureThreshold = 1,
                PendingAlerts = new PendingAlertOptions { Enabled = true }
            }));
        var job = CreateJob(new AlertRule
        {
            Name = "every",
            Condition = AlertConditionType.EveryFailure,
            Channels = ["email"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        Assert.Equal(1, channel.SendCount);

        channel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Equal(2, channel.SendCount);
    }

    [Fact]
    public async Task PendingBacklogDropsOldestBeyondMaxEntries()
    {
        var logger = new ListLogger<AlertRuleProcessor>();
        var channel = new TestChannel("email") { Result = NotificationResult.Fail("down") };
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(),
            new NotificationDispatcher([channel]),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions
            {
                FailureThreshold = 1,
                PendingAlerts = new PendingAlertOptions { Enabled = true, MaxEntries = 2 }
            }),
            logger: logger);
        var job = CreateJob(new AlertRule
        {
            Name = "every",
            Condition = AlertConditionType.EveryFailure,
            Channels = ["email"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        await processor.ProcessAsync(job, FailedResult(2), default);
        await processor.ProcessAsync(job, FailedResult(3), default);
        Assert.Equal(6, channel.SendCount);
        Assert.Contains(logger.Messages, message => message.Contains("dropping the oldest entry"));

        channel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(4), default);

        Assert.Equal(8, channel.SendCount);
    }

    [Fact]
    public async Task JobLockTableExhaustionFailsLoudly()
    {
        var processor = CreateProcessor([new TestChannel("email")], threshold: 1);
        for (var index = 0; index < 4096; index++)
        {
            var job = CreateJob();
            job.Name = $"job-{index}";
            await processor.ProcessAsync(job, SuccessfulResult(1), default);
        }

        var overflow = CreateJob();
        overflow.Name = "job-overflow";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(overflow, SuccessfulResult(2), default));
        Assert.Contains("job-lock table is full", exception.Message);
    }

    [Fact]
    public async Task RuleWithEmptyChannelsIsRejected()
    {
        var logger = new ListLogger<AlertRuleProcessor>();
        var channel = new TestChannel("email");
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(),
            new NotificationDispatcher([channel]),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions { FailureThreshold = 1 }),
            logger: logger);
        var job = CreateJob(new AlertRule
        {
            Name = "no-channels",
            Condition = AlertConditionType.StateConfirmed,
            Channels = []
        });

        await processor.ProcessAsync(job, FailedResult(1), default);

        Assert.Empty(channel.Messages);
        Assert.Contains(logger.Messages, message => message.Contains("Channels"));
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
    public async Task StringEnumRuleConfigurationDeserializesAndFires()
    {
        var channel = new TestChannel("email");
        var processor = CreateProcessor([channel], threshold: 1);
        var job = new ScheduledJob
        {
            Name = "test-job",
            ResultProcessorConfig = JsonDocument.Parse("""
                {"Rules":[{"Name":"confirmed","Condition":"StateConfirmed","Severity":"Critical",
                           "Channels":["email"],"Recipients":["+1234567890"],
                           "TemplateCode":"ALERT_DOWN","Throttle":"00:15:00"}]}
                """).RootElement.Clone()
        };

        await processor.ProcessAsync(job, FailedResult(1), default);

        var message = Assert.Single(channel.Messages);
        Assert.Equal(NotificationSeverity.Critical, message.Severity);
        Assert.Equal(["+1234567890"], message.Recipients);
        Assert.Equal("ALERT_DOWN", message.TemplateCode);
    }

    [Fact]
    public async Task RuleRecipientsAndTemplateCodeFlowToTheNotification()
    {
        var channel = new TestChannel("sms");
        var processor = CreateProcessor([channel], threshold: 1);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["sms"],
            Recipients = ["+1234567890"],
            TemplateCode = "ALERT_DOWN"
        });

        await processor.ProcessAsync(job, FailedResult(1), default);

        var message = Assert.Single(channel.Messages);
        Assert.Equal(["+1234567890"], message.Recipients);
        Assert.Equal("ALERT_DOWN", message.TemplateCode);
    }

    [Fact]
    public async Task RuleTemplateParametersFlowWithBuiltInsAndOverrideWins()
    {
        var channel = new TestChannel("sms");
        var processor = CreateProcessor([channel], threshold: 1);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["sms"],
            TemplateParameters = new Dictionary<string, string> { ["code"] = "8000", ["job"] = "declared-name" }
        });

        await processor.ProcessAsync(job, FailedResult(1), default);

        var message = Assert.Single(channel.Messages);
        Assert.Equal("8000", message.TemplateParameters["code"]);
        Assert.Equal("declared-name", message.TemplateParameters["job"]);
        Assert.Equal("503", message.TemplateParameters["status"]);
        Assert.Equal("Unhealthy", message.TemplateParameters["state"]);
    }

    [Fact]
    public async Task RuleTemplateParametersDeserializeFromJson()
    {
        var channel = new TestChannel("sms");
        var processor = CreateProcessor([channel], threshold: 1);
        var job = new ScheduledJob
        {
            Name = "test-job",
            ResultProcessorConfig = JsonDocument.Parse("""
                {"Rules":[{"Name":"confirmed","Condition":"StateConfirmed","Channels":["sms"],
                           "TemplateParameters":{"code":"8000","product":"demo-app"}}]}
                """).RootElement.Clone()
        };

        await processor.ProcessAsync(job, FailedResult(1), default);

        var message = Assert.Single(channel.Messages);
        Assert.Equal("8000", message.TemplateParameters["code"]);
        Assert.Equal("demo-app", message.TemplateParameters["product"]);
    }

    [Fact]
    public async Task TemplateParametersNumericValuesSurviveConfigurationMaterialization()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "test-job",
                ["Jobs:0:ResultProcessorConfig:Rules:0:Name"] = "confirmed",
                ["Jobs:0:ResultProcessorConfig:Rules:0:Condition"] = "StateConfirmed",
                ["Jobs:0:ResultProcessorConfig:Rules:0:Channels:0"] = "sms",
                ["Jobs:0:ResultProcessorConfig:Rules:0:TemplateParameters:code"] = "8000",
            }).Build();
        var bound = SchedulerConfigurationBinding.Bind(cfg);
        var channel = new TestChannel("sms");
        var processor = CreateProcessor([channel], threshold: 1);

        await processor.ProcessAsync(bound.Jobs[0], FailedResult(1), default);

        var message = Assert.Single(channel.Messages);
        Assert.Equal("8000", message.TemplateParameters["code"]);
    }

    [Fact]
    public async Task UnparseableRuleEntryReportsItsIndex()
    {
        var logger = new ListLogger<AlertRuleProcessor>();
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(),
            new NotificationDispatcher([]),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions { FailureThreshold = 1 }),
            logger: logger);
        var job = new ScheduledJob
        {
            Name = "test-job",
            ResultProcessorConfig = JsonDocument.Parse("""
                {"Rules":[{"Name":"ok","Condition":"StateConfirmed","Channels":["email"]},
                          {"Name":"bad","Throttle":{"x":1}}]}
                """).RootElement.Clone()
        };

        await processor.ProcessAsync(job, FailedResult(1), default);

        Assert.Contains(logger.Messages, message => message.Contains("Alert rule 1"));
    }

    [Fact]
    public async Task PendingAlertResendsUndeliveredMessageOnNextExecution()
    {
        var channel = new TestChannel("dingtalk") { Result = NotificationResult.Fail("down") };
        var processor = CreateProcessor([channel], threshold: 1,
            configure: options => options.PendingAlerts.Enabled = true);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["dingtalk"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        Assert.Single(channel.Messages);

        channel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Equal(2, channel.SendCount);
        Assert.Equal(2, channel.Messages.Count);
        Assert.Equal(channel.Messages[0].CorrelationId, channel.Messages[1].CorrelationId);
    }

    [Fact]
    public async Task UndeliveredAlertIsNotRetriedWhenPendingDisabled()
    {
        var channel = new TestChannel("dingtalk") { Result = NotificationResult.Fail("down") };
        var processor = CreateProcessor([channel], threshold: 1);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["dingtalk"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        channel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Equal(1, channel.SendCount);
    }

    [Fact]
    public async Task PendingAlertRecordsOnlyTheFailedChannels()
    {
        var delivered = new TestChannel("email");
        var failing = new TestChannel("dingtalk") { Result = NotificationResult.Fail("down") };
        var processor = CreateProcessor([delivered, failing], threshold: 1,
            configure: options => options.PendingAlerts.Enabled = true);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["dingtalk", "email"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        failing.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(2), default);

        Assert.Equal(1, delivered.SendCount);
        Assert.Equal(2, failing.SendCount);
    }

    [Fact]
    public async Task PendingAlertExpiresAfterConfiguredWindow()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var logger = new ListLogger<AlertRuleProcessor>();
        var channel = new TestChannel("dingtalk") { Result = NotificationResult.Fail("down") };
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(clock),
            new NotificationDispatcher([channel], timeProvider: clock),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions
            {
                FailureThreshold = 1,
                PendingAlerts = new PendingAlertOptions { Enabled = true, Expiry = TimeSpan.FromMinutes(10) }
            }),
            clock,
            logger);
        var job = CreateJob(new AlertRule
        {
            Name = "confirmed",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["dingtalk"]
        });

        await processor.ProcessAsync(job, FailedResult(1), default);
        clock.Advance(TimeSpan.FromMinutes(9));
        await processor.ProcessAsync(job, SuccessfulResult(2), default);
        Assert.Equal(2, channel.SendCount);

        clock.Advance(TimeSpan.FromMinutes(2));
        channel.Result = NotificationResult.Ok();
        await processor.ProcessAsync(job, SuccessfulResult(3), default);

        Assert.Equal(2, channel.SendCount);
        Assert.Contains(logger.Messages, message => message.Contains("Dropping expired pending alert"));
    }

    [Fact]
    public async Task StartupLintReportsInvalidRuleWithJobName()
    {
        var logger = new ListLogger<AlertRuleLintHostedService>();
        var job = CreateJob(new AlertRule
        {
            Name = " ",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["dingtalk"]
        });
        job.ResultProcessors.Add(AlertRuleProcessor.ProcessorName);
        var skipped = CreateJob(new AlertRule
        {
            Name = " ",
            Condition = AlertConditionType.StateConfirmed,
            Channels = ["dingtalk"]
        });
        skipped.Name = "unrelated-job";
        var lint = new AlertRuleLintHostedService(
            Options.Create(new SchedulerOptions { Jobs = [job, skipped] }),
            logger);

        await lint.StartAsync(default);

        Assert.Contains(logger.Messages, message => message.Contains("test-job") && message.Contains("invalid"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("unrelated-job"));
    }

    [Fact]
    public async Task InvalidRuleConfigurationReportsTheRuleIndexAndReason()
    {
        var logger = new ListLogger<AlertRuleProcessor>();
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(),
            new NotificationDispatcher([]),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions { FailureThreshold = 1 }),
            logger: logger);
        var job = new ScheduledJob
        {
            Name = "test-job",
            ResultProcessorConfig = JsonDocument.Parse("""
                {"Rules":[{"Name":"a","Condition":"StateConfirmed","Channels":["email"]},
                          {"Name":"a","Condition":"EveryFailure","Channels":["email"]}]}
                """).RootElement.Clone()
        };

        await processor.ProcessAsync(job, FailedResult(1), default);

        Assert.Contains(logger.Messages, message =>
            message.Contains("Alert rule 1") && message.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MalformedRuleJsonReportsTheJsonError()
    {
        var logger = new ListLogger<AlertRuleProcessor>();
        var processor = new AlertRuleProcessor(
            new MemoryStateStore(),
            new NotificationDispatcher([]),
            new TestProbe(),
            Options.Create(new AlertProcessorOptions { FailureThreshold = 1 }),
            logger: logger);
        var job = new ScheduledJob
        {
            Name = "test-job",
            ResultProcessorConfig = JsonDocument.Parse("""{"Rules":[{"Condition":"bogus"}]}""").RootElement.Clone()
        };

        await processor.ProcessAsync(job, FailedResult(1), default);

        Assert.Contains(logger.Messages, message => message.Contains("not valid JSON", StringComparison.OrdinalIgnoreCase));
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
        TimeProvider? timeProvider = null,
        Action<AlertProcessorOptions>? configure = null)
    {
        var options = new AlertProcessorOptions { FailureThreshold = threshold };
        configure?.Invoke(options);
        return new(
            new MemoryStateStore(timeProvider),
            new NotificationDispatcher(channels, timeProvider: timeProvider),
            probe ?? new TestProbe(),
            Options.Create(options),
            timeProvider);
    }

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
            lock (Messages)
            {
                SendCount++;
                Messages.Add(message);
            }
            return Task.FromResult(Result);
        }
    }

    private sealed record StubTenantContext(long? TenantId) : PubSoft.Domain.ITenantContext;

    private sealed class FakeDbContext : PubSoft.EntityFrameworkCore.PubSoftDbContext
    {
        public FakeDbContext(DbContextOptions options)
            : base(options, new StubTenantContext(null), null, null, null) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftSchedulerState();
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}