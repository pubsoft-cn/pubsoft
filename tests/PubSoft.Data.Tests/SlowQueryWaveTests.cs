using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SlowQueryWaveTests
{
    [Fact]
    public void CtorAcceptsThresholdAndLogger()
    {
        var i = new SlowQueryInterceptor(TimeSpan.FromMilliseconds(200), NullLogger<SlowQueryInterceptor>.Instance);
        Assert.NotNull(i);
    }

    [Fact]
    public void CtorWithoutLoggerValid()
        => Assert.NotNull(new SlowQueryInterceptor(TimeSpan.FromSeconds(1)));

    [Fact]
    public void CtorZeroThresholdValid()
        => Assert.NotNull(new SlowQueryInterceptor(TimeSpan.Zero)); // 0ms → logs every query
}
