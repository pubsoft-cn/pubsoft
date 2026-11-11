using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching.Redis;
using PubSoft.Monitoring;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>Redis connection posture diagnostics coverage.</summary>
public sealed class RedisDiagnosticsWaveTests
{
    private const string RedisEnv = "PUBSOFT_REDIS";
    private static string? RedisConn => System.Environment.GetEnvironmentVariable(RedisEnv);

    [Fact]
    public void UnregisteredMultiplexerWarns()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var entries = new RedisConnectionStartupDiagnostics(sp).Collect().ToList();
        var entry = Assert.Single(entries);
        Assert.Equal(StartupDiagnosticsStatus.Warning, entry.Status);
        Assert.Contains("IConnectionMultiplexer", entry.Detail);
    }

    [Fact]
    public async Task RegisteredMultiplexerReportsOk()
    {
        if (RedisConn is null) return; // needs PUBSOFT_REDIS
        await using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        var sp = new ServiceCollection().AddSingleton<IConnectionMultiplexer>(mux).BuildServiceProvider();
        var entries = new RedisConnectionStartupDiagnostics(sp).Collect().ToList();
        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, e => e.Status == StartupDiagnosticsStatus.Error);
    }
}
