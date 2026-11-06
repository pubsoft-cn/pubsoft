namespace PubSoft.Caching;

/// <summary>Distributed mutex used by scheduler/reconcile flows; implemented per-store.</summary>
public interface IDistributedLock
{
    /// <summary>Acquires a lock on <paramref name="key"/>; null when the lock is held elsewhere after waiting.</summary>
    Task<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default);
}
