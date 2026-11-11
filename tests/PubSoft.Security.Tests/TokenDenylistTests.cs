using Microsoft.Extensions.DependencyInjection;
using PubSoft.Authentication.Jwt.AspNetCore;
using PubSoft.Security;
using Xunit;

namespace PubSoft.Security.Tests;

/// <summary>Token denylist: deny/check/expiry/capacity + bearer denylist wiring.</summary>
public sealed class TokenDenylistTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan span) => _now += span;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public async Task DeniedTokenReportsDeniedUntilExpiry()
    {
        var clock = new ManualClock();
        var denylist = new MemoryTokenDenylist(timeProvider: clock);
        var id = TokenDenyIds.ForToken("compact.jwt.value");

        Assert.False(await denylist.IsDeniedAsync(id));
        await denylist.DenyAsync(new TokenDenyEntry(id, "subject-1", clock.GetUtcNow() + TimeSpan.FromMinutes(5), "logout"));
        Assert.True(await denylist.IsDeniedAsync(id));

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(await denylist.IsDeniedAsync(id));
    }

    [Fact]
    public async Task ExpiredDenyEntryIsNoOp()
    {
        var clock = new ManualClock();
        var denylist = new MemoryTokenDenylist(timeProvider: clock);
        var id = TokenDenyIds.ForToken("already.expired");
        await denylist.DenyAsync(new TokenDenyEntry(id, null, clock.GetUtcNow() - TimeSpan.FromMinutes(1), null));
        Assert.False(await denylist.IsDeniedAsync(id));
    }

    [Fact]
    public async Task CapacityFailsClosed()
    {
        var clock = new ManualClock();
        var denylist = new MemoryTokenDenylist(maxEntries: 1, timeProvider: clock);
        var until = clock.GetUtcNow() + TimeSpan.FromMinutes(10);
        await denylist.DenyAsync(new TokenDenyEntry("id-1", null, until, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denylist.DenyAsync(new TokenDenyEntry("id-2", null, until, null)).AsTask());
    }

    [Fact]
    public async Task SweepFreesCapacityForLiveEntries()
    {
        var clock = new ManualClock();
        var denylist = new MemoryTokenDenylist(maxEntries: 1, timeProvider: clock);
        await denylist.DenyAsync(new TokenDenyEntry("id-old", null, clock.GetUtcNow() + TimeSpan.FromMinutes(1), null));
        clock.Advance(TimeSpan.FromMinutes(2));
        await denylist.DenyAsync(new TokenDenyEntry("id-new", null, clock.GetUtcNow() + TimeSpan.FromMinutes(5), null));
        Assert.True(await denylist.IsDeniedAsync("id-new"));
    }

    [Fact]
    public async Task InputBoundsAreEnforced()
    {
        var denylist = new MemoryTokenDenylist();
        await Assert.ThrowsAsync<ArgumentException>(() => denylist.IsDeniedAsync(string.Empty).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => denylist.DenyAsync(new TokenDenyEntry(new string('x', 129), null, DateTimeOffset.UtcNow.AddMinutes(1), null)).AsTask());
        Assert.Throws<ArgumentException>(() => TokenDenyIds.ForToken(string.Empty));
    }

    [Fact]
    public void TokenDenyIdsProducesStableHash()
    {
        var a = TokenDenyIds.ForToken("token-value");
        var b = TokenDenyIds.ForToken("token-value");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.NotEqual(TokenDenyIds.ForToken("other"), a);
    }

    [Fact]
    public void MemoryRegistrationResolvesDenylist()
    {
        var sp = new ServiceCollection().AddPubSoftMemoryTokenDenylist().BuildServiceProvider();
        Assert.IsAssignableFrom<MemoryTokenDenylist>(sp.GetRequiredService<ITokenDenylist>());
    }

    [Fact]
    public void BearerCheckDenylistRequiresRegisteredDenylist()
    {
        var builder = new ServiceCollection().AddAuthentication();
        Assert.Throws<InvalidOperationException>(() => builder.AddPubSoftJwtBearer(o =>
        {
            o.Issuer = "iss";
            o.Audience = "aud";
            o.SymmetricKey = new byte[32];
            o.CheckDenylist = true;
        }));
    }

    [Fact]
    public void BearerCheckDenylistAcceptsRegisteredDenylist()
    {
        var builder = new ServiceCollection()
            .AddPubSoftMemoryTokenDenylist()
            .AddAuthentication();
        var result = builder.AddPubSoftJwtBearer(o =>
        {
            o.Issuer = "iss";
            o.Audience = "aud";
            o.SymmetricKey = new byte[32];
            o.CheckDenylist = true;
        });
        Assert.NotNull(result);
    }
}
