using PubSoft.Security;
using PubSoft.Security.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RefreshTokenFamilyStoreContractTests
{
    [Fact]
    public async Task MemoryStore_ConcurrentConsumesResolveToSingleWinner()
    {
        var store = new MemoryRefreshTokenFamilyStore();
        var hash = await SaveTokenAsync(store, "memory-race");

        var results = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => store.TryConsumeAsync(hash).AsTask()));

        Assert.Single(results, result => result == RefreshConsumeResult.Consumed);
        Assert.Equal(31, results.Count(result => result == RefreshConsumeResult.AlreadyConsumed));
        Assert.True((await store.GetByHashAsync(hash))!.Consumed);
    }

    [Fact]
    public async Task MemoryStore_RevokedFamilyCannotConsume()
    {
        var store = new MemoryRefreshTokenFamilyStore();
        var hash = await SaveTokenAsync(store, "memory-revoked");
        var record = (await store.GetByHashAsync(hash))!;

        await store.RevokeFamilyAsync(record.FamilyId, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(RefreshConsumeResult.FamilyRevoked, await store.TryConsumeAsync(hash));
        Assert.False((await store.GetByHashAsync(hash))!.Consumed);
    }

    [RedisFact]
    public async Task RedisStore_ConcurrentConsumesResolveToSingleWinnerAcrossInstances()
    {
        var configuration = Environment.GetEnvironmentVariable("PUBSOFT_REDIS")
            ?? throw new InvalidOperationException("Redis tests are enabled without PUBSOFT_REDIS.");
        await using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        var options = new RedisRefreshTokenFamilyStoreOptions { KeyPrefix = $"pubsoft:rtf-contract:{Guid.NewGuid():N}:" };
        var first = new RedisRefreshTokenFamilyStore(connection, options);
        var second = new RedisRefreshTokenFamilyStore(connection, options);
        var hash = await SaveTokenAsync(first, "redis-race");
        var familyId = (await first.GetByHashAsync(hash))!.FamilyId;
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(index => (index % 2 == 0 ? first : second).TryConsumeAsync(hash).AsTask()));

            Assert.Single(results, result => result == RefreshConsumeResult.Consumed);
            Assert.Equal(31, results.Count(result => result == RefreshConsumeResult.AlreadyConsumed));
        }
        finally
        {
            await CleanupKeysAsync(connection, options.KeyPrefix, familyId, hash);
        }
    }

    [RedisFact]
    public async Task RedisStore_MissingTokenRecordCannotBeRecreatedByConsumeRace()
    {
        var configuration = Environment.GetEnvironmentVariable("PUBSOFT_REDIS")
            ?? throw new InvalidOperationException("Redis tests are enabled without PUBSOFT_REDIS.");
        await using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        var options = new RedisRefreshTokenFamilyStoreOptions { KeyPrefix = $"pubsoft:rtf-contract:{Guid.NewGuid():N}:" };
        var store = new RedisRefreshTokenFamilyStore(connection, options);
        var hash = await SaveTokenAsync(store, "redis-missing-record");
        var record = (await store.GetByHashAsync(hash))!;
        var tokenKey = options.KeyPrefix + "{f:" + record.FamilyId + "}:tok:" + hash;
        Assert.True(await connection.GetDatabase().KeyDeleteAsync(tokenKey));

        var result = await store.TryConsumeAsync(hash);

        Assert.Equal(RefreshConsumeResult.AlreadyConsumed, result);
        Assert.False(await connection.GetDatabase().KeyExistsAsync(tokenKey));
        await CleanupKeysAsync(connection, options.KeyPrefix, record.FamilyId, hash);
    }

    private static async Task CleanupKeysAsync(IConnectionMultiplexer connection, string prefix, string familyId, string hash)
    {
        var database = connection.GetDatabase();
        foreach (RedisKey key in new RedisKey[]
        {
            prefix + "{f:" + familyId + "}:tok:" + hash,
            prefix + "{f:" + familyId + "}:fam",
            prefix + "tokptr:" + hash,
            prefix + "sub:contract-subject"
        })
            await database.KeyDeleteAsync(key);
    }

    private static async Task<string> SaveTokenAsync(IRefreshTokenFamilyStore store, string familyId)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = TokenDenyIds.ForToken("token-" + familyId + Guid.NewGuid().ToString("N"));
        await store.SaveAsync(new RefreshTokenFamilyRecord(
            hash,
            familyId,
            "contract-subject",
            now,
            now.AddMinutes(10),
            now.AddHours(1),
            Consumed: false));
        return hash;
    }
}
