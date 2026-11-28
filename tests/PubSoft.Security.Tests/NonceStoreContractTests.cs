using PubSoft.Testing;
using Xunit;

namespace PubSoft.Security.Tests;

public sealed class NonceStoreContractTests
{
    [Fact]
    public async Task MemoryStore_ConcurrentReservationsResolveToSingleWinner()
    {
        var store = new MemoryNonceStore();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => store.TryUseAsync("contract", "shared-nonce", expiresAt).AsTask()));

        Assert.Single(results, accepted => accepted);
        Assert.Equal(63, results.Count(accepted => !accepted));
    }

    [Fact]
    public async Task MemoryStore_ScopesAreIndependentAndExpiredNonceCanBeReservedAgain()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(now);
        var store = new MemoryNonceStore(timeProvider: time);

        Assert.True(await store.TryUseAsync("first", "nonce", now.AddSeconds(10)));
        Assert.True(await store.TryUseAsync("second", "nonce", now.AddSeconds(10)));
        Assert.False(await store.TryUseAsync("first", "nonce", now.AddSeconds(10)));

        time.Advance(TimeSpan.FromSeconds(11));
        Assert.True(await store.TryUseAsync("first", "nonce", now.AddSeconds(20)));
    }

    [Fact]
    public async Task MemoryStore_CapacityFailsClosedAfterExpiredSweep()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(now);
        var store = new MemoryNonceStore(maxEntries: 1, timeProvider: time);
        Assert.True(await store.TryUseAsync("contract", "live", now.AddMinutes(5)));

        Assert.False(await store.TryUseAsync("contract", "other", now.AddMinutes(5)));

        time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await store.TryUseAsync("contract", "other", time.GetUtcNow().AddMinutes(5)));
    }

    [Fact]
    public async Task MemoryStore_RejectsAlreadyExpiredNonce()
    {
        var store = new MemoryNonceStore();

        Assert.False(await store.TryUseAsync("contract", "expired", DateTimeOffset.UtcNow.AddSeconds(-1)));
    }
}
