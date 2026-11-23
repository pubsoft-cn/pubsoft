using Microsoft.Extensions.Configuration;
using PubSoft.Scheduling;
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
