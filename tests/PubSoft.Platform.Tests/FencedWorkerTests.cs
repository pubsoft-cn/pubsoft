using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Caching;
using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Platform.Tests;

public sealed class FencedExecutorTests
{
    private sealed class FakeFencedLock : IDistributedLock
    {
        public long NextToken = 41;
        public Task<IAsyncDisposable?> AcquireAsync(
            string key, TimeSpan expiry, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable?>(new Handle(key, NextToken));

        private sealed class Handle(string key, long token) : IFencedLockHandle
        {
            public string Key => key;
            public long FencingToken => token;
            public Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
                => Task.FromResult(true);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteAsyncRunsWorkAndSurfacesFencingToken()
    {
        var executor = new FencedExecutor(new FakeFencedLock());
        FencedWorkContext? seen = null;
        var ran = await executor.TryExecuteAsync("k", TimeSpan.FromMinutes(1),
            (ctx, _) => { seen = ctx; return Task.CompletedTask; });
        Assert.True(ran);
        Assert.Equal("k", seen!.LockKey);
        Assert.Equal(41, seen.FencingToken);
    }

    [Fact]
    public async Task NonFencingLockYieldsNullToken()
    {
        var executor = new FencedExecutor(new InProcessDistributedLock());
        FencedWorkContext? seen = null;
        await executor.TryExecuteAsync("k", TimeSpan.FromMinutes(1),
            (ctx, _) => { seen = ctx; return Task.CompletedTask; });
        Assert.Null(seen!.FencingToken);
    }

    [Fact]
    public async Task HeldLockSkipsWorkByDefault()
    {
        // no waitTimeout passed: the "try" contract skips a held lock instead of queueing
        var sharedLock = new InProcessDistributedLock();
        await using var held = await sharedLock.AcquireAsync("k", TimeSpan.FromMinutes(1));
        var executor = new FencedExecutor(sharedLock);
        var ran = false;
        var acquired = await executor.TryExecuteAsync("k", TimeSpan.FromMinutes(1),
            (_, _) => { ran = true; return Task.CompletedTask; });
        Assert.False(acquired);
        Assert.False(ran);
    }

    [Fact]
    public async Task ExplicitWaitTimeoutBlocksUntilReleased()
    {
        var sharedLock = new InProcessDistributedLock();
        await using var held = await sharedLock.AcquireAsync("k", TimeSpan.FromMinutes(1));
        Assert.NotNull(held);
        var executor = new FencedExecutor(sharedLock);
        var ran = false;
        var attempt = executor.TryExecuteAsync("k", TimeSpan.FromMinutes(1),
            (_, _) => { ran = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(5));
        await Task.Delay(100); // still blocked while the other holder has the lock
        Assert.False(attempt.IsCompleted);
        Assert.False(ran);
        await held.DisposeAsync();
        Assert.True(await attempt);
        Assert.True(ran);
    }

    [Fact]
    public async Task LockReleasedAfterWork()
    {
        var sharedLock = new InProcessDistributedLock();
        var executor = new FencedExecutor(sharedLock);
        await executor.TryExecuteAsync("k", TimeSpan.FromMinutes(1),
            (_, _) => Task.CompletedTask);
        // re-acquisition succeeds immediately only if the first hold was released
        var acquired = await executor.TryExecuteAsync("k", TimeSpan.FromMinutes(1),
            (_, _) => Task.CompletedTask, TimeSpan.Zero);
        Assert.True(acquired);
    }
}

public sealed class FencedWorkerTests
{
    [Fact]
    public async Task RegisteredWorkerRunsWorkEachInterval()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPubSoftInProcessDistributedLock();
        services.AddPubSoftFencedWorker("sweep", (_, _) =>
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        }, o =>
        {
            o.Interval = TimeSpan.FromMilliseconds(50);
            o.RunOnStartup = true;
        });
        await using var sp = services.BuildServiceProvider();
        await sp.GetServices<IHostedService>().OfType<FencedWorkerHostedService>().Single().StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);
    }

    [Fact]
    public void OptionsValidationRejectsBadValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection()
            .AddPubSoftFencedWorker("w", (_, _) => Task.CompletedTask, o => o.Interval = TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection()
            .AddPubSoftFencedWorker("w", (_, _) => Task.CompletedTask, o => o.LockExpiry = TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new ServiceCollection()
            .AddPubSoftFencedWorker(" ", (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task WorkExceptionsDoNotKillTheLoop()
    {
        var calls = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPubSoftInProcessDistributedLock();
        services.AddPubSoftFencedWorker("flaky", (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
                tcs.TrySetResult();
            throw new InvalidOperationException("boom");
        }, o =>
        {
            o.Interval = TimeSpan.FromMilliseconds(20);
            o.RunOnStartup = true;
        });
        await using var sp = services.BuildServiceProvider();
        await sp.GetServices<IHostedService>().OfType<FencedWorkerHostedService>().Single().StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);
        Assert.True(calls >= 2);
    }
}
