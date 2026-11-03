using PubSoft.Caching;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class InProcessDistributedLockTests
{
    [Fact]
    public async Task Acquire_ExcludesConcurrentHoldersUntilRelease()
    {
        var mutex = new InProcessDistributedLock();
        await using var first = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1));
        Assert.NotNull(first);

        var contended = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50));
        Assert.Null(contended);

        await first.DisposeAsync();
        await using var second = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Acquire_ZeroWaitTimeoutIsSingleNonBlockingTry()
    {
        var mutex = new InProcessDistributedLock();
        await using var held = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.NotNull(held);

        Assert.Null(await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.Zero));

        var other = await mutex.AcquireAsync("other", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.NotNull(other);
        await other!.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_ExpiredHoldIsForceReleasedForWaiter()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var mutex = new InProcessDistributedLock(timeProvider: time);
        var stale = await mutex.AcquireAsync("key", TimeSpan.FromSeconds(30));
        Assert.NotNull(stale);

        time.Advance(TimeSpan.FromSeconds(31));
        var waiter = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2));
        Assert.NotNull(waiter);

        // The stale holder's release is a no-op; the lock stays with the new owner.
        await stale!.DisposeAsync();
        var contended = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.Null(contended);

        await waiter!.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_UnexpiredHoldIsNotForceReleased()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var mutex = new InProcessDistributedLock(timeProvider: time);
        await using var held = await mutex.AcquireAsync("key", TimeSpan.FromSeconds(60));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.Zero));
    }

    [Fact]
    public async Task Acquire_DifferentKeysAreIndependent()
    {
        var mutex = new InProcessDistributedLock();
        var first = await mutex.AcquireAsync("a", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        var second = await mutex.AcquireAsync("b", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.NotNull(first);
        Assert.NotNull(second);
        await first!.DisposeAsync();
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_AtCapacityFailsClosed()
    {
        var mutex = new InProcessDistributedLock(maxEntries: 1);
        await using var held = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1));
        Assert.NotNull(held);

        Assert.Null(await mutex.AcquireAsync("other", TimeSpan.FromMinutes(1), TimeSpan.Zero));

        await held!.DisposeAsync();
        var afterRelease = await mutex.AcquireAsync("other", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.NotNull(afterRelease);
        await afterRelease!.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_HonorsCancellation()
    {
        var mutex = new InProcessDistributedLock();
        await using var held = await mutex.AcquireAsync("key", TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mutex.AcquireAsync("key", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public async Task RejectsInvalidArguments()
    {
        var mutex = new InProcessDistributedLock();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => mutex.AcquireAsync("key", TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(
            () => mutex.AcquireAsync("", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Dispose_RejectsNewAcquisitions()
    {
        var mutex = new InProcessDistributedLock();
        mutex.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => mutex.AcquireAsync("key", TimeSpan.FromMinutes(1)));
    }
}
