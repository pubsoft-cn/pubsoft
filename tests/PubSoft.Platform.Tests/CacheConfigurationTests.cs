using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching;
using PubSoft.Caching.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Platform.Tests;

public sealed class CacheConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void MemoryProviderResolvesMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddPubSoftCache(Config(("Caching:Provider", "memory")));
        using var sp = services.BuildServiceProvider();
        Assert.IsType<MemoryCache>(sp.GetRequiredService<ICache>());
    }

    [Fact]
    public void MissingProviderFailsClosed()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddPubSoftCache(Config()));
        Assert.Contains("Caching:Provider", ex.Message);
    }

    [Fact]
    public void UnknownProviderFailsClosed()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPubSoftCache(Config(("Caching:Provider", "postgres"))));
        Assert.Contains("postgres", ex.Message);
    }

    [Fact]
    public void RedisProviderWithoutConnectionStringFails()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPubSoftCache(Config(("Caching:Provider", "redis"))));
        Assert.Contains("Caching:Redis:ConnectionString", ex.Message);
    }

    private sealed class RedisFactAttribute : FactAttribute
    {
        public RedisFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_REDIS")))
                Skip = "Set PUBSOFT_REDIS to run Redis integration tests.";
        }
    }

    [RedisFact]
    public async Task RedisProviderResolvesRedisCache()
    {
        var connection = Environment.GetEnvironmentVariable("PUBSOFT_REDIS")!;
        var services = new ServiceCollection();
        services.AddPubSoftCache(Config(
            ("Caching:Provider", "redis"),
            ("Caching:Redis:ConnectionString", connection),
            ("Caching:Redis:KeyPrefix", "cfg-test:")));
        await using var sp = services.BuildServiceProvider();
        Assert.IsType<RedisCache>(sp.GetRequiredService<ICache>());
    }

    [Fact]
    public void RedisProviderDefersConnectUntilResolution()
    {
        var services = new ServiceCollection();
        // An unreachable broker must not block or fail the composition root.
        services.AddPubSoftCache(Config(
            ("Caching:Provider", "redis"),
            ("Caching:Redis:ConnectionString", "invalid-host.invalid:6379,connectTimeout=1")));
        Assert.NotNull(services);
    }

    [RedisFact]
    public async Task RedisMultiplexerIsContainerOwned()
    {
        var connection = Environment.GetEnvironmentVariable("PUBSOFT_REDIS")!;
        var services = new ServiceCollection();
        services.AddPubSoftCache(Config(
            ("Caching:Provider", "redis"),
            ("Caching:Redis:ConnectionString", connection)));

        IConnectionMultiplexer mux;
        await using (var sp = services.BuildServiceProvider())
        {
            mux = sp.GetRequiredService<IConnectionMultiplexer>();
            Assert.True(mux.IsConnected);
        }
        Assert.False(mux.IsConnected); // the container owned - and disposed - the connection
    }
}
