namespace PubSoft.Scheduling;

/// <summary>Runs work under a distributed lock; returns false when the lock is held elsewhere.</summary>
public interface IFencedExecutor
{
    /// <summary>
    /// Acquires <paramref name="lockKey"/> and runs <paramref name="work"/> under it, releasing on exit.
    /// Returns false when the lock could not be acquired within <paramref name="waitTimeout"/>.
    /// </summary>
    /// <param name="lockKey">The distributed-lock key to acquire.</param>
    /// <param name="lockExpiry">How long the lock lease lasts before it auto-releases.</param>
    /// <param name="work">The work to run under the lock; receives the fenced execution context.</param>
    /// <param name="waitTimeout">
    /// <see cref="TimeSpan.Zero"/> (default): a held lock skips the attempt entirely;
    /// a finite value waits up to that bound; <see cref="Timeout.InfiniteTimeSpan"/> waits
    /// until the lock is acquired or <paramref name="cancellationToken"/> fires.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait for the lock or the running work.</param>
    Task<bool> TryExecuteAsync(
        string lockKey,
        TimeSpan lockExpiry,
        Func<FencedWorkContext, CancellationToken, Task> work,
        TimeSpan waitTimeout = default,
        CancellationToken cancellationToken = default);
}
