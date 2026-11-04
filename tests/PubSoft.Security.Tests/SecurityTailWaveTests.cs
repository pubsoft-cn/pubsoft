using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SecurityTailWaveTests
{
    [Fact]
    public void RateLimiterFixedWindowDeniesOverLimit()
    {
        var r = new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 2, LimitByIpAddress = true });
        Assert.True(r.CheckRateLimit("id1").IsAllowed);
        Assert.True(r.CheckRateLimit("id1").IsAllowed);
        var third = r.CheckRateLimit("id1");
        Assert.False(third.IsAllowed);
        Assert.Equal("rate_limit_exceeded", third.RejectionReason);
    }

    [Fact]
    public void RateLimiterPeekWithoutCount()
    {
        var r = new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 1 });
        r.CheckRateLimit("x");
        var peek = r.CheckRateLimit("x", countRequest: false);
        Assert.False(peek.IsAllowed); // already consumed, peek doesn't add
    }

    [Fact]
    public void RateLimiterDisabledAllowsAll()
    {
        var r = new RateLimiter(new RateLimiterOptions { Enabled = false });
        var res = r.CheckRateLimit("any");
        Assert.True(res.IsAllowed);
        Assert.Equal(int.MaxValue, res.RemainingRequests);
    }

    [Fact]
    public void RateLimiterRejectsInvalidIdentifier()
    {
        var r = new RateLimiter();
        Assert.False(r.CheckRateLimit(null).IsAllowed);
        Assert.False(r.CheckRateLimit("   ").IsAllowed);
        Assert.Equal("invalid_identifier", r.CheckRateLimit(" ").RejectionReason);
    }

    [Fact]
    public void RateLimiterResetAndCleanup()
    {
        var r = new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 1 });
        r.CheckRateLimit("a");
        r.ResetRateLimit("a");
        Assert.True(r.CheckRateLimit("a").IsAllowed); // reset cleared
        r.CleanupExpiredEntries();
        Assert.NotNull(r.GetStats());
    }

    [Fact]
    public void HmacSha256Deterministic()
    {
        var s1 = HmacSignatureValidator.ComputeHmacSha256("k", "data");
        Assert.Equal(s1, HmacSignatureValidator.ComputeHmacSha256("k", "data"));
        Assert.NotEqual(s1, HmacSignatureValidator.ComputeHmacSha256("k2", "data"));
        Assert.Equal(64, s1.Length); // hex sha256
    }

    [Fact]
    public void DataMaskingOptionsChars()
    {
        var o = new DataMaskingOptions();
        Assert.NotEqual('\0', o.EmailMaskChar);
        Assert.NotEqual('\0', o.PhoneMaskChar);
        Assert.NotEqual('\0', o.IdCardMaskChar);
        Assert.NotEqual('\0', o.BankCardMaskChar);
        Assert.NotEqual('\0', o.GeneralMaskChar);
    }
}
