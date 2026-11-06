using PubSoft.Identity;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class IdentityUserLockoutWaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AccessFailuresLockoutAtThresholdThenReset()
    {
        var user = new IdentityUser(1, "u");

        user.RecordAccessFailure(3, TimeSpan.FromMinutes(5), Now);
        user.RecordAccessFailure(3, TimeSpan.FromMinutes(5), Now);
        Assert.Equal(2, user.AccessFailedCount);
        Assert.Null(user.LockoutEnd);
        Assert.False(user.IsLockedOut(Now));

        user.RecordAccessFailure(3, TimeSpan.FromMinutes(5), Now);
        Assert.Equal(0, user.AccessFailedCount); // streak ends at lockout
        Assert.Equal(Now.AddMinutes(5), user.LockoutEnd);
        Assert.True(user.IsLockedOut(Now.AddMinutes(4)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(6)));

        user.ResetAccessFailures();
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public void ExtJsonPassesThroughUnvalidated()
    {
        var user = new IdentityUser(1, "u") { ExtJson = "{\"avatar\":\"x\"}" };
        Assert.Equal("{\"avatar\":\"x\"}", user.ExtJson);
    }
}
