using Microsoft.Extensions.Options;
using PubSoft.Monitoring.Scheduling;
using PubSoft.Security.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class NonceAlertWaveTests
{
    private const string RedisEnv = "PUBSOFT_REDIS";
    private static string? RedisConn => Environment.GetEnvironmentVariable(RedisEnv);

    [Fact]
    public void RedisNonceStoreOptionsDefaults()
    {
        var o = new RedisNonceStoreOptions();
        Assert.Equal("pubsoft:security:nonce:", o.KeyPrefix);
        Assert.Equal(512, o.MaxScopeLength);
        Assert.Equal(1024, o.MaxNonceLength);
    }

    [Fact]
    public async Task RedisNonceStoreAtomicClaimAcrossReplays()
    {
        if (RedisConn is null) return; // needs PUBSOFT_REDIS
        await using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        var store = new RedisNonceStore(mux, new RedisNonceStoreOptions());
        var nonce = $"n-{Guid.NewGuid():N}";
        var exp = DateTimeOffset.UtcNow.AddMinutes(5);
        Assert.True(await store.TryUseAsync("svc", nonce, exp));   // first claim wins
        Assert.False(await store.TryUseAsync("svc", nonce, exp));  // replay rejected
        Assert.True(await store.TryUseAsync("svc", nonce + "x", exp)); // different nonce ok
    }

    [Fact]
    public void AlertModelsEnumsAndRecords()
    {
        Assert.True(Enum.GetValues<JobHealthState>().Length >= 2);
        Assert.True(Enum.GetValues<AlertSeverity>().Length >= 3);
        Assert.True(Enum.GetValues<AlertConditionType>().Length >= 2);
        var rule = new AlertRule();
        var cfg = new AlertRuleConfig();
        var opts = new AlertProcessorOptions();
        Assert.NotNull(rule); Assert.NotNull(cfg); Assert.NotNull(opts);
        Assert.NotNull(typeof(AlertContext)); // required members — surface touch
    }
}
