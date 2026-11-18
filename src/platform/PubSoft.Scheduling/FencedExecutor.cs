using PubSoft.Caching;

namespace PubSoft.Scheduling;


/// <summary>Default <see cref="IFencedExecutor"/> over the registered <see cref="IDistributedLock"/>.</summary>
public sealed class FencedExecutor : IFencedExecutor
{
    private readonly IDistributedLock _distributedLock;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the executor.</summary>
    public FencedExecutor(IDistributedLock distributedLock, TimeProvider? timeProvider = null)
    {
        _distributedLock = distributedLock ?? throw new ArgumentNullException(nameof(distributedLock));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<bool> TryExecuteAsync(
        string lockKey,
        TimeSpan lockExpiry,
        Func<FencedWorkContext, CancellationToken, Task> work,
        TimeSpan waitTimeout = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var handle = await _distributedLock
            .AcquireAsync(lockKey, lockExpiry, waitTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (handle is null)
            return false;

        var context = new FencedWorkContext(
            lockKey,
            handle is IFencedLockHandle fenced ? fenced.FencingToken : null,
            _timeProvider.GetUtcNow());
        await work(context, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
