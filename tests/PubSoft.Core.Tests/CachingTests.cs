using PubSoft.Caching;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CachingTests
{
    [Fact]
    public async Task SetAndGet_RoundTripsValues()
    {
        using var cache = new MemoryCache(TimeSpan.FromMinutes(1));

        await cache.SetAsync("user:1", "Ada");

        Assert.Equal("Ada", await cache.GetAsync<string>("user:1"));
        Assert.True(await cache.ExistsAsync("user:1"));
    }

    [Fact]
    public async Task ExpiredValues_AreNotVisible()
    {
        using var cache = new MemoryCache(TimeSpan.FromMinutes(1));
        await cache.SetAsync("short", "value", TimeSpan.FromMilliseconds(20));
        await Task.Delay(60);

        Assert.Null(await cache.GetAsync<string>("short"));
        Assert.False(await cache.ExistsAsync("short"));
    }

    [Fact]
    public async Task RemoveByPattern_UsesEscapedGlobSemantics()
    {
        using var cache = new MemoryCache(TimeSpan.FromMinutes(1));
        await cache.SetManyAsync(new Dictionary<string, string>
        {
            ["user.1"] = "one",
            ["user.12"] = "twelve",
            ["userX1"] = "other",
        });

        await cache.RemoveByPatternAsync("user.?" );

        Assert.False(await cache.ExistsAsync("user.1"));
        Assert.True(await cache.ExistsAsync("user.12"));
        Assert.True(await cache.ExistsAsync("userX1"));
    }

    [Fact]
    public async Task Cache_EnforcesConfiguredCapacity()
    {
        using var cache = new MemoryCache(TimeSpan.FromMinutes(1), maxSize: 3);
        await cache.SetManyAsync(Enumerable.Range(0, 20)
            .Select(index => new KeyValuePair<string, string>($"item:{index}", index.ToString())));

        Assert.True(cache.GetStatistics().TotalEntries <= 3);
    }

    [Fact]
    public async Task GetOrSet_CoalescesConcurrentFactoriesPerKey()
    {
        using var cache = new MemoryCache(TimeSpan.FromMinutes(1));
        var factoryCalls = 0;

        var requests = Enumerable.Range(0, 32)
            .Select(_ => cache.GetOrSetAsync("shared", async () =>
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(30);
                return "value";
            }));

        var values = await Task.WhenAll(requests);

        Assert.All(values, value => Assert.Equal("value", value));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void Cache_RejectsInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryCache(maxSize: 0));
    }
}
