namespace PubSoft.Caching;

/// <summary>A lock handle carrying the fencing token assigned at acquisition.</summary>
/// <remarks>
/// The fencing token is a strictly increasing counter per lock key. A lease alone is not a
/// correctness guarantee -- a paused or partitioned holder may lose its lease while believing
/// it still owns the lock. The protected resource must persist and compare the token
/// (reject writes whose token is not greater than the last seen value).
/// </remarks>
public interface IFencedLockHandle : IAsyncDisposable
{
    /// <summary>The locked key.</summary>
    string Key { get; }
    /// <summary>Monotonic fencing token assigned at acquisition.</summary>
    long FencingToken { get; }

    /// <summary>Extends the lease while still owned; returns false when ownership was already lost.</summary>
    Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default);
}
