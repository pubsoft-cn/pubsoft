using Microsoft.Extensions.DependencyInjection;
using PubSoft.Security;
using PubSoft.Security.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>Redis token denylist: shared revocation source of truth (integration).</summary>
public sealed class TokenDenylistWaveTests
{
    private const string RedisEnv = "PUBSOFT_REDIS";
    private static string? RedisConn => System.Environment.GetEnvironmentVariable(RedisEnv);

    [Fact]
    public void RegistrationRequiresMultiplexerAndValidOptions()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddPubSoftRedisTokenDenylist(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddPubSoftRedisTokenDenylist(null!, o => o.KeyPrefix = string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddPubSoftRedisTokenDenylist(null!, o => o.MaxTokenIdLength = 0));
    }

    [Fact]
    public async Task DenyPropagatesAcrossInstances()
    {
        if (RedisConn is null) return; // needs PUBSOFT_REDIS
        await using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        var options = new RedisTokenDenylistOptions { KeyPrefix = $"pubsoft:deny:{Guid.NewGuid():N}:" };
        var writer = new RedisTokenDenylist(mux, options);
        var reader = new RedisTokenDenylist(mux, options); // second logical instance

        var id = TokenDenyIds.ForToken("cross-instance-token");
        Assert.False(await reader.IsDeniedAsync(id));
        await writer.DenyAsync(new TokenDenyEntry(id, "subject-1", DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5), "logout"));
        Assert.True(await reader.IsDeniedAsync(id));
    }
}
