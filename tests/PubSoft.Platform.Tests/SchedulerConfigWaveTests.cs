using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PubSoft.Scheduling;
using PubSoft.Scheduling.Http;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulerConfigWaveTests
{
    [Fact]
    public void SchedulerOptionsDefaults()
    {
        var o = new SchedulerOptions();
        Assert.Equal(TimeSpan.FromSeconds(1), o.LoopInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), o.ErrorDelay);
        Assert.Equal(16, o.MaxConcurrentJobs);
    }

    [Fact]
    public void BindReadsConfigSection()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LoopInterval"] = "00:00:10",
                ["MaxConcurrentJobs"] = "4",
            }).Build();
        var o = SchedulerConfigurationBinding.Bind(cfg);
        Assert.Equal(TimeSpan.FromSeconds(10), o.LoopInterval);
        Assert.Equal(4, o.MaxConcurrentJobs);
    }

    [Fact]
    public void BindScheduleIntervalShortcut()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "sweep",
                ["Jobs:0:Schedule:EveryMinutes"] = "5",
            }).Build();

        var o = SchedulerConfigurationBinding.Bind(cfg);

        Assert.Equal(ScheduleMasks.EveryMinutes(5), o.Jobs[0].Schedule);
    }

    [Fact]
    public void BindScheduleBitmaskFieldsStillWork()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "hourly",
                ["Jobs:0:Schedule:Seconds"] = "1",
                ["Jobs:0:Schedule:Minutes"] = "1",
                ["Jobs:0:Schedule:Hours"] = "16777215",
                ["Jobs:0:Schedule:DaysOfMonth"] = "4294967294",
                ["Jobs:0:Schedule:Months"] = "8190",
                ["Jobs:0:Schedule:DaysOfWeek"] = "127",
            }).Build();

        var o = SchedulerConfigurationBinding.Bind(cfg);

        Assert.Equal(ScheduleMask.EveryHour, o.Jobs[0].Schedule);
    }

    [Fact]
    public void BindRejectsCronUnknownKeysAndMixedScheduleForms()
    {
        var cron = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "a",
                ["Jobs:0:Schedule:Cron"] = "*/5 * * * *",
            }).Build();
        Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(cron));

        var typo = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "a",
                ["Jobs:0:Schedule:Minute"] = "1",
            }).Build();
        Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(typo));

        var mixed = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "a",
                ["Jobs:0:Schedule:EveryMinutes"] = "5",
                ["Jobs:0:Schedule:Minutes"] = "1",
            }).Build();
        Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(mixed));

        var scalar = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:0:Name"] = "a",
                ["Jobs:0:Schedule"] = "*/5 * * * *",
            }).Build();
        Assert.Throws<FormatException>(() => SchedulerConfigurationBinding.Bind(scalar));
    }

    [Fact]
    public void ToJsonElementPreservesNumericLookingStrings()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutorConfig:Headers:Tid"] = "8000",
                ["ExecutorConfig:Headers:Pad"] = "007",
                ["ExecutorConfig:Headers:Plus"] = "+1234567890",
                ["ExecutorConfig:Headers:Flag"] = "true",
                ["ExecutorConfig:Headers:Floaty"] = "12.50",
                ["ExecutorConfig:Timeout"] = "00:00:30",
                ["ExecutorConfig:ExpectStatusCodes:0"] = "401",
            }).Build();

        var element = SchedulerConfigurationBinding.ToJsonElement(cfg.GetSection("ExecutorConfig"));

        Assert.Equal(JsonValueKind.Number, element.GetProperty("Headers").GetProperty("Tid").ValueKind);
        Assert.Equal(JsonValueKind.String, element.GetProperty("Headers").GetProperty("Pad").ValueKind);
        Assert.Equal(JsonValueKind.String, element.GetProperty("Headers").GetProperty("Plus").ValueKind);
        Assert.Equal(JsonValueKind.True, element.GetProperty("Headers").GetProperty("Flag").ValueKind);
        Assert.Equal(JsonValueKind.String, element.GetProperty("Headers").GetProperty("Floaty").ValueKind);
        Assert.Equal(JsonValueKind.Number, element.GetProperty("ExpectStatusCodes")[0].ValueKind);

        var config = element.Deserialize(SchedulingHttpJsonContext.Default.HttpExecutorConfig)!;
        Assert.Equal("8000", config.Headers["Tid"]);
        Assert.Equal("007", config.Headers["Pad"]);
        Assert.Equal("+1234567890", config.Headers["Plus"]);
        Assert.Equal("true", config.Headers["Flag"]);
        Assert.Equal("12.50", config.Headers["Floaty"]);
        Assert.Equal([401], config.ExpectStatusCodes);
        Assert.Equal(TimeSpan.FromSeconds(30), config.Timeout);
    }

    [Fact]
    public void DescribeRendersReadableScheduleFields()
    {
        Assert.Equal(
            "seconds={0} minutes=* hours=* days-of-month=* months=* days-of-week=*",
            ScheduleMask.EveryMinute.Describe());
        Assert.Equal(
            "seconds={0} minutes={0} hours=* days-of-month=* months=* days-of-week=*",
            ScheduleMask.EveryHour.Describe());
        Assert.Equal(
            "seconds=* minutes={0} hours=* days-of-month=* months=* days-of-week=*",
            new ScheduleMask((1UL << 60) - 1, 1UL, (1U << 24) - 1, 0xFFFFFFFE, 0x1FFE, 0x7F).Describe());
    }
}
