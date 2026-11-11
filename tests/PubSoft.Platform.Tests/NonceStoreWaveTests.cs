using Microsoft.Extensions.DependencyInjection;
using PubSoft.Security;
using PubSoft.Security.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>Redis nonce store registration + claim semantics (integration).</summary>
public sealed class NonceStoreWaveTests
{
    private const string RedisEnv = "PUBSOFT_REDIS";
    private static string? RedisConn => System.Environment.GetEnvironmentVariable(RedisEnv);

    [Fact]
    public void RegistrationRequiresMultiplexer()
    {
        var services = new ServiceCollection();
        // extension signature demands the multiplexer up front - fail-closed registration
        Assert.Throws<ArgumentNullException>(() => services.AddPubSoftRedisNonceStore(null!));
    }

    [Fact]
    public async Task RegisteredStoreClaimsAndRejectsReplay()
    {
        if (RedisConn is null) return; // needs PUBSOFT_REDIS
        await using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        var sp = new ServiceCollection().AddPubSoftRedisNonceStore(mux).BuildServiceProvider();
        var store = Assert.IsAssignableFrom<INonceStore>(sp.GetService<INonceStore>());
        var nonce = $"cov-{Guid.NewGuid():N}";
        Assert.True(await store.TryUseAsync("test", nonce, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.False(await store.TryUseAsync("test", nonce, DateTimeOffset.UtcNow.AddMinutes(1))); // replay rejected
    }
}
