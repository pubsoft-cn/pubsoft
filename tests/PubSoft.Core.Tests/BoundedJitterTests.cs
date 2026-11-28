using Xunit;

namespace PubSoft.Core.Tests;

public sealed class BoundedJitterTests
{
    [Fact]
    public void SamplesStayInsideTheSymmetricBand()
    {
        var baseDelay = TimeSpan.FromSeconds(5);
        for (var i = 0; i < 500; i++)
        {
            var sampled = BoundedJitter.Apply(baseDelay, 0.2);
            Assert.InRange(sampled, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6));
        }
    }

    [Fact]
    public void ZeroRatioReturnsBaseDelayExactly()
    {
        var baseDelay = TimeSpan.FromSeconds(5);
        Assert.Equal(baseDelay, BoundedJitter.Apply(baseDelay, 0));
    }

    [Fact]
    public void ZeroBaseDelayStaysZero()
    {
        Assert.Equal(TimeSpan.Zero, BoundedJitter.Apply(TimeSpan.Zero, 0.5));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRatioIsRejected(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedJitter.Apply(TimeSpan.FromSeconds(1), ratio));
    }

    [Fact]
    public void SamplesActuallyVary()
    {
        var baseDelay = TimeSpan.FromSeconds(5);
        var distinct = new HashSet<TimeSpan>();
        for (var i = 0; i < 50; i++)
            distinct.Add(BoundedJitter.Apply(baseDelay, 0.2));
        Assert.True(distinct.Count > 1, "jitter produced identical samples every time");
    }
}
