using PubSoft.Messaging.Outbox;
using PubSoft.Monitoring.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OptionsDefaultsWaveTests
{
    [Fact]
    public void OutboxOptionsDefaults()
    {
        var o = new OutboxOptions();
        Assert.Equal(50, o.BatchSize);
        Assert.Equal(5, o.MaxAttempts);
        Assert.Equal(1_048_576, o.MaximumPayloadCharacters);
        Assert.Equal(TimeSpan.FromMinutes(1), o.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), o.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), o.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromHours(1), o.MaxRetryDelay);
    }

    [Fact]
    public void AlertProcessorOptionsDefaults()
        => Assert.Equal(3, new AlertProcessorOptions().FailureThreshold);
}
