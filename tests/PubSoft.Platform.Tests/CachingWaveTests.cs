using PubSoft.Caching;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CachingWaveTests
{
    private sealed class Item { public int V { get; set; } }

    [Fact]
    public async Task MemoryCacheCrud()
    {
        using var c = new MemoryCache();
        Assert.Null(await c.GetAsync<Item>("k"));
        await c.SetAsync("k", new Item { V = 7 });
        Assert.True(await c.ExistsAsync("k"));
        Assert.Equal(7, (await c.GetAsync<Item>("k"))!.V);
        await c.RemoveAsync("k");
        Assert.False(await c.ExistsAsync("k"));
    }

    [Fact]
    public async Task GetOrSetAsyncCachesFactoryResult()
    {
        using var c = new MemoryCache();
        var calls = 0;
        var v1 = await c.GetOrSetAsync("k", () => { calls++; return Task.FromResult(new Item { V = 1 }); });
        var v2 = await c.GetOrSetAsync("k", () => { calls++; return Task.FromResult(new Item { V = 2 }); });
        Assert.Equal(1, calls); // second call served from cache
        Assert.Equal(1, v2.V);
    }

    [Fact]
    public async Task GetManyAndSetManyRoundtrip()
    {
        using var c = new MemoryCache();
        await c.SetManyAsync<Item>([new("a", new Item { V = 1 }), new("b", new Item { V = 2 })]);
        var many = await c.GetManyAsync<Item>(["a", "b", "c"]);
        Assert.Equal(3, many.Count);
        Assert.Equal(1, many["a"]!.V);
        Assert.Null(many["c"]);
    }

    [Fact]
    public async Task RemoveByPatternClearsMatchingKeys()
    {
        using var c = new MemoryCache();
        await c.SetAsync("user:1", new Item { V = 1 });
        await c.SetAsync("user:2", new Item { V = 2 });
        await c.SetAsync("other:1", new Item { V = 3 });
        await c.RemoveByPatternAsync("user:*");
        Assert.False(await c.ExistsAsync("user:1"));
        Assert.True(await c.ExistsAsync("other:1"));
    }

    [Fact]
    public async Task InProcessDistributedLockMutualExclusion()
    {
        using var l = new InProcessDistributedLock();
        await using var h1 = await l.AcquireAsync("r", TimeSpan.FromMinutes(1));
        Assert.NotNull(h1);
        var h2 = await l.AcquireAsync("r", TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50));
        Assert.Null(h2); // held → can't re-acquire within wait timeout
        var h3 = await l.AcquireAsync("other", TimeSpan.FromMinutes(1));
        Assert.NotNull(h3); // different resource ok
    }

    [Fact]
    public void CacheStatisticsShape()
    {
        var s = new MemoryCache.CacheStatistics { TotalEntries = 5, ExpiredEntries = 1, SizeLimit = 100 };
        Assert.Equal(5, s.TotalEntries);
        Assert.Equal(100, s.SizeLimit);
    }
}
