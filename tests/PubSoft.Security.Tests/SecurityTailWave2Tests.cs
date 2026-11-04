using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SecurityTailWave2Tests
{
    [Fact]
    public void ResultMembersAccessible()
    {
        var r = new RateLimitResult { IsAllowed = true, RemainingRequests = 5, ResetTime = DateTime.UtcNow, RejectionReason = null };
        Assert.True(r.IsAllowed);
        Assert.Equal(5, r.RemainingRequests);

        var s = new RateLimiter.RateLimitStats { TotalIdentifiers = 3, ActiveIdentifiers = 2, TotalRequests = 10 };
        Assert.Equal(3, s.TotalIdentifiers);
        Assert.Equal(2, s.ActiveIdentifiers);
        Assert.Equal(10, s.TotalRequests);

        var h = new HmacSignatureResult { IsValid = true, AppId = "app1" };
        Assert.True(h.IsValid);
        Assert.Equal("app1", h.AppId);

        var p = new PasswordVerificationResult(true, "rehash!");
        Assert.True(p.Succeeded);
        Assert.Equal("rehash!", p.ReplacementHash);
    }

}
