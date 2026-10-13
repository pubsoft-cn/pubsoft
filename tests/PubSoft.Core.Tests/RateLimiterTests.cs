using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RateLimiterTests
{
    [Fact]
    public void CheckRateLimit_EnforcesWindowLimitUnderConcurrency()
    {
        var limiter = new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 32 });
        var results = new RateLimitResult[2_000];
        Parallel.For(0, results.Length, index => results[index] = limiter.CheckRateLimit("client"));

        Assert.Equal(32, results.Count(result => result.IsAllowed));
        Assert.Equal(1_968, results.Count(result => result.RejectionReason == "rate_limit_exceeded"));
        Assert.Equal(32, limiter.GetStats().TotalRequests);
    }

    [Fact]
    public void CheckRateLimit_FailsClosedForInvalidOrOverCapacityIdentifiers()
    {
        var limiter = new RateLimiter(new RateLimiterOptions { MaxIdentifiers = 1 });

        Assert.False(limiter.CheckRateLimit(null).IsAllowed);
        Assert.True(limiter.CheckRateLimit("first").IsAllowed);
        Assert.Equal("identifier_capacity_exceeded", limiter.CheckRateLimit("second").RejectionReason);
    }

    [Fact]
    public void Constructor_ValidatesAndSnapshotsOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(new RateLimiterOptions { WindowSize = TimeSpan.Zero }));

        var options = new RateLimiterOptions { RequestsPerWindow = 1 };
        var limiter = new RateLimiter(options);
        options.RequestsPerWindow = 20;

        Assert.True(limiter.CheckRateLimit("client").IsAllowed);
        Assert.False(limiter.CheckRateLimit("client").IsAllowed);
    }

    [Fact]
    public void CheckRateLimit_CanCheckWithoutConsumingARequest()
    {
        var limiter = new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 1 });

        Assert.True(limiter.CheckRateLimit("client", countRequest: false).IsAllowed);
        Assert.Equal(0, limiter.GetStats().TotalRequests);
        Assert.True(limiter.CheckRateLimit("client").IsAllowed);
        Assert.False(limiter.CheckRateLimit("client", countRequest: false).IsAllowed);
        Assert.Equal(1, limiter.GetStats().TotalRequests);
    }
}