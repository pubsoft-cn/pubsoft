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
}
