namespace PubSoft.Caching;

public interface IDistributedLock
{
    Task<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default);
}
