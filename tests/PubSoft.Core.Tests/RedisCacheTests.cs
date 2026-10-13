using System.Text.Json;
using PubSoft.Caching;
using PubSoft.Caching.Redis;
using PubSoft.Security.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RedisCacheTests
{
    [RedisFact]
    public async Task RedisNonceStoreAllowsExactlyOneCrossConnectionReservationAndExpires()
    {
        await using var fixture = await RedisFixture.ConnectAsync();
        using var secondConnection = await ConnectionMultiplexer.ConnectAsync(fixture.Configuration);
        var options = new RedisNonceStoreOptions { KeyPrefix = fixture.Prefix + "nonce:" };
        var firstStore = new RedisNonceStore(fixture.Connection, options);
        var secondStore = new RedisNonceStore(secondConnection, options);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(10);

        var reservations = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(index => (index % 2 == 0 ? firstStore : secondStore)
                .TryUseAsync("app", "shared-nonce", expiresAt).AsTask()));

        Assert.Single(reservations, accepted => accepted);
        Assert.Equal(63, reservations.Count(accepted => !accepted));
        Assert.True(await firstStore.TryUseAsync("another-app", "shared-nonce", expiresAt));

        var soonExpires = DateTimeOffset.UtcNow.AddMilliseconds(400);
        Assert.True(await firstStore.TryUseAsync("app", "short-lived", soonExpires));
        await Task.Delay(500);
        Assert.True(await secondStore.TryUseAsync("app", "short-lived", DateTimeOffset.UtcNow.AddSeconds(5)));
    }

    [RedisFact]
    public async Task SingleAndBatchOperations_RoundTripValues()
    {
        await using var fixture = await RedisFixture.ConnectAsync();
        var cache = fixture.Cache;

        await cache.SetAsync("single", new CachedProfile("Ada", 37));
        await cache.SetManyAsync(new Dictionary<string, CachedProfile>
        {
            ["batch:1"] = new("Grace", 40),
            ["batch:2"] = new("Linus", 55),
        });

        Assert.Equal(new CachedProfile("Ada", 37), await cache.GetAsync<CachedProfile>("single"));
        Assert.True(await cache.ExistsAsync("single"));
        var values = await cache.GetManyAsync<CachedProfile>(["batch:1", "batch:2", "missing"]);
        Assert.Equal(new CachedProfile("Grace", 40), values["batch:1"]);
        Assert.Equal(new CachedProfile("Linus", 55), values["batch:2"]);
        Assert.Null(values["missing"]);

        await cache.RemoveManyAsync(["single", "batch:1"]);

        Assert.False(await cache.ExistsAsync("single"));
        Assert.False(await cache.ExistsAsync("batch:1"));
        Assert.True(await cache.ExistsAsync("batch:2"));
    }

    [RedisFact]
    public async Task SetAsync_AppliesExpiry()
    {
        await using var fixture = await RedisFixture.ConnectAsync();

        await fixture.Cache.SetAsync("short", new CachedProfile("Ada", 37), TimeSpan.FromMilliseconds(100));
        await Task.Delay(250);

        Assert.Null(await fixture.Cache.GetAsync<CachedProfile>("short"));
        Assert.False(await fixture.Cache.ExistsAsync("short"));
    }

    [RedisFact]
    public async Task RemoveByPattern_UsesGlobAndOnlyDeletesWithinConfiguredPrefix()
    {
        await using var fixture = await RedisFixture.ConnectAsync();
        await fixture.Cache.SetManyAsync(new Dictionary<string, CachedProfile>
        {
            ["user:1"] = new("one", 1),
            ["user:12"] = new("twelve", 12),
            ["userX1"] = new("other", 3),
        });
        await fixture.OtherPrefixCache.SetAsync("user:1", new CachedProfile("isolated", 0));

        await fixture.Cache.RemoveByPatternAsync("user:?");

        Assert.False(await fixture.Cache.ExistsAsync("user:1"));
        Assert.True(await fixture.Cache.ExistsAsync("user:12"));
        Assert.True(await fixture.Cache.ExistsAsync("userX1"));
        Assert.True(await fixture.OtherPrefixCache.ExistsAsync("user:1"));
    }

    [RedisFact]
    public async Task GetOrSet_UsesCachedValueWithoutCallingFactory()
    {
        await using var fixture = await RedisFixture.ConnectAsync();
        await fixture.Cache.SetAsync("cached", new CachedProfile("Ada", 37));
        var factoryCalls = 0;

        var result = await fixture.Cache.GetOrSetAsync("cached", () =>
        {
            factoryCalls++;
            return Task.FromResult(new CachedProfile("replacement", 0));
        });

        Assert.Equal(new CachedProfile("Ada", 37), result);
        Assert.Equal(0, factoryCalls);
    }

    [RedisFact]
    public async Task Operations_ValidateInputsAndHonorPreCanceledToken()
    {
        await using var fixture = await RedisFixture.ConnectAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Cache.GetAsync<CachedProfile>("key", cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Cache.GetAsync<CachedProfile>(" "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Cache.SetAsync("key", new CachedProfile("Ada", 1), TimeSpan.Zero));
        await Assert.ThrowsAsync<JsonException>(() => fixture.CorruptAsync());
    }

    private sealed record CachedProfile(string Name, int Age);

    private sealed class RedisFixture : IAsyncDisposable
    {
        private readonly IConnectionMultiplexer _connection;

        private RedisFixture(IConnectionMultiplexer connection, string prefix, string configuration)
        {
            _connection = connection;
            Cache = new RedisCache(connection, new RedisCacheOptions { KeyPrefix = prefix });
            OtherPrefixCache = new RedisCache(connection, new RedisCacheOptions { KeyPrefix = $"{prefix}other:" });
            Prefix = prefix;
            Configuration = configuration;
        }

        public RedisCache Cache { get; }
        public RedisCache OtherPrefixCache { get; }
        public string Prefix { get; }
        public string Configuration { get; }
        public IConnectionMultiplexer Connection => _connection;

        public static async Task<RedisFixture> ConnectAsync()
        {
            var configuration = Environment.GetEnvironmentVariable("PUBSOFT_REDIS");
            if (string.IsNullOrWhiteSpace(configuration))
                throw new InvalidOperationException("Redis tests are enabled without PUBSOFT_REDIS.");

            var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            return new RedisFixture(connection, $"pubsoft:test:{Guid.NewGuid():N}:", configuration);
        }

        public async Task CorruptAsync()
        {
            await _connection.GetDatabase().StringSetAsync(Prefix + "corrupt", "not-json");
            await Cache.GetAsync<CachedProfile>("corrupt");
        }

        public async ValueTask DisposeAsync()
        {
            await Cache.RemoveByPatternAsync("*");
            await OtherPrefixCache.RemoveByPatternAsync("*");
            await _connection.CloseAsync();
            _connection.Dispose();
        }
    }
}

public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_REDIS")))
            Skip = "Set PUBSOFT_REDIS to run Redis integration tests.";
    }
}