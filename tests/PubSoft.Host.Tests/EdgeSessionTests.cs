using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeSessionTests
{
    [Fact]
    public async Task Create_And_Validate_Should_RoundTrip()
    {
        var manager = new EdgeSessionManager(new InMemoryEdgeSessionStore(), TimeSpan.FromHours(1));
        var key = await manager.CreateAsync("client1", "app1", "jwt-token", userId: "u1", memberId: "m1", roles: ["admin"]);
        var session = await manager.ValidateAsync(key);

        Assert.NotNull(session);
        Assert.Equal("jwt-token", session!.Token);
        Assert.Equal("u1", session.UserId);
        Assert.Equal("m1", session.MemberId);
        Assert.Contains("admin", session.Roles);
    }

    [Fact]
    public void SessionKey_Format_Should_Be_Client_App_Guid()
    {
        var key = EdgeSessionManager.GenerateSessionKey("myclient", "myapp");
        var parts = key.Split(':');
        Assert.Equal(3, parts.Length);
        Assert.Equal("myclient", parts[0]);
        Assert.Equal("myapp", parts[1]);
        Assert.Equal(32, parts[2].Length);
    }

    [Fact]
    public async Task Validate_With_Unknown_Key_Should_Return_Null()
    {
        var manager = new EdgeSessionManager(new InMemoryEdgeSessionStore());
        Assert.Null(await manager.ValidateAsync("nonexistent:key:xyz"));
    }

    [Fact]
    public async Task Revoke_Should_Invalidate_Session()
    {
        var manager = new EdgeSessionManager(new InMemoryEdgeSessionStore());
        var key = await manager.CreateAsync("c", "a", "t");
        await manager.RevokeAsync(key);
        Assert.Null(await manager.ValidateAsync(key));
    }

    [Fact]
    public async Task Expired_Session_Should_Return_Null()
    {
        var manager = new EdgeSessionManager(new InMemoryEdgeSessionStore(), TimeSpan.FromMilliseconds(-1));
        var key = await manager.CreateAsync("c", "a", "t");
        Assert.Null(await manager.ValidateAsync(key));
    }

    [Fact]
    public async Task Validate_Should_AutoRenew_By_Default()
    {
        var store = new InMemoryEdgeSessionStore();
        var manager = new EdgeSessionManager(store, TimeSpan.FromHours(1));
        var key = await manager.CreateAsync("c", "a", "t", expiry: TimeSpan.FromMilliseconds(50));
        var before = (await store.GetAsync(key))!.ExpiresAt;
        await manager.ValidateAsync(key);
        var after = (await store.GetAsync(key))!.ExpiresAt;
        Assert.True(after > before);
    }
}
