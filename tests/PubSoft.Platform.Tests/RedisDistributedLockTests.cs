using PubSoft.Caching.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RedisDistributedLockTests
{
    private sealed class LockFixture : IAsyncDisposable
    {
        private LockFixture(IConnectionMultiplexer connection, string prefix, string configuration)
        {
            Connection = connection;
            Prefix = prefix;
            Configuration = configuration;
        }

        public IConnectionMultiplexer Connection { get; }
        public string Prefix { get; }
        public string Configuration { get; }

        public static async Task<LockFixture> ConnectAsync()
        {
            var configuration = Environment.GetEnvironmentVariable("PUBSOFT_REDIS")
                ?? throw new InvalidOperationException("Redis tests are enabled without PUBSOFT_REDIS.");
            var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            return new LockFixture(connection, $"pubsoft:test:{Guid.NewGuid():N}:", configuration);
        }

        public async ValueTask DisposeAsync()
        {
            // 集群模式逐节点 SCAN+DEL——KEYS+unpack 在 cluster 下不可用（跨 slot 报错）
            var db = Connection.GetDatabase();
            foreach (var endpoint in Connection.GetEndPoints())
            {
                var server = Connection.GetServer(endpoint);
                var keys = server.KeysAsync(pattern: Prefix + "*");
                var batch = db.CreateBatch();
                var tasks = new List<Task>();
                await foreach (var key in keys)
                    tasks.Add(batch.KeyDeleteAsync(key));
                batch.Execute();
                await Task.WhenAll(tasks);
            }
            await Connection.CloseAsync();
            Connection.Dispose();
        }
    }

    [RedisFact]
    public async Task Acquire_MutuallyExcludesAcrossIndependentConnections()
    {
        await using var fixture = await LockFixture.ConnectAsync();
        using var secondConnection = await ConnectionMultiplexer.ConnectAsync(fixture.Configuration);
        var options = new RedisDistributedLockOptions { KeyPrefix = fixture.Prefix + "lock:" };
        var first = new RedisDistributedLock(fixture.Connection, options);
        var second = new RedisDistributedLock(secondConnection, options);

        var acquisitions = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(index => (index % 2 == 0 ? first : second)
                .AcquireAsync("shared", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5))));

        Assert.Single(acquisitions, handle => handle is not null);
        var winner = Assert.Single(acquisitions, handle => handle is not null);
        await winner!.DisposeAsync();

        var afterRelease = await second.AcquireAsync("shared", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        Assert.NotNull(afterRelease);
        await afterRelease!.DisposeAsync();
    }

    [RedisFact]
    public async Task FencingTokensIncreaseAcrossAcquisitions()
    {
        await using var fixture = await LockFixture.ConnectAsync();
        var mutex = new RedisDistributedLock(
            fixture.Connection, new RedisDistributedLockOptions { KeyPrefix = fixture.Prefix + "lock:" });

        var fences = new List<long>();
        for (var index = 0; index < 3; index++)
        {
            var handle = Assert.IsAssignableFrom<IFencedLockHandle>(
                await mutex.AcquireAsync("fenced", TimeSpan.FromMinutes(1)));
            fences.Add(handle.FencingToken);
            await handle.DisposeAsync();
        }

        Assert.Equal(fences.Order().ToList(), fences);
        Assert.Equal(3, fences.Distinct().Count());
    }

    [RedisFact]
    public async Task StaleHolderReleaseDoesNotDeleteNewOwnersLease()
    {
        await using var fixture = await LockFixture.ConnectAsync();
        var mutex = new RedisDistributedLock(
            fixture.Connection, new RedisDistributedLockOptions { KeyPrefix = fixture.Prefix + "lock:" });

        var stale = await mutex.AcquireAsync("stale", TimeSpan.FromMilliseconds(300));
        Assert.NotNull(stale);
        await Task.Delay(400);

        var current = await mutex.AcquireAsync("stale", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        Assert.NotNull(current);

        await stale!.DisposeAsync();
        Assert.Null(await mutex.AcquireAsync("stale", TimeSpan.FromMinutes(1), TimeSpan.Zero));

        await current!.DisposeAsync();
    }

    [RedisFact]
    public async Task ExtendKeepsOwnershipAndFailsAfterLoss()
    {
        await using var fixture = await LockFixture.ConnectAsync();
        var mutex = new RedisDistributedLock(
            fixture.Connection, new RedisDistributedLockOptions { KeyPrefix = fixture.Prefix + "lock:" });

        var handle = Assert.IsAssignableFrom<IFencedLockHandle>(
            await mutex.AcquireAsync("extend", TimeSpan.FromMilliseconds(400)));
        Assert.True(await handle.ExtendAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(350);
        Assert.Null(await mutex.AcquireAsync("extend", TimeSpan.FromMinutes(1), TimeSpan.Zero));

        await handle.DisposeAsync();
        Assert.False(await handle.ExtendAsync(TimeSpan.FromSeconds(5)));
    }

    [RedisFact]
    public async Task Acquire_ZeroWaitTimeoutIsSingleTryAndArgumentsAreValidated()
    {
        await using var fixture = await LockFixture.ConnectAsync();
        var mutex = new RedisDistributedLock(
            fixture.Connection, new RedisDistributedLockOptions { KeyPrefix = fixture.Prefix + "lock:" });

        var held = await mutex.AcquireAsync("once", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.NotNull(held);
        Assert.Null(await mutex.AcquireAsync("once", TimeSpan.FromMinutes(1), TimeSpan.Zero));
        await held!.DisposeAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => mutex.AcquireAsync("once", TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(
            () => mutex.AcquireAsync("", TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RedisDistributedLock(
                fixture.Connection,
                new RedisDistributedLockOptions { FenceRetention = TimeSpan.Zero }));
    }
}
