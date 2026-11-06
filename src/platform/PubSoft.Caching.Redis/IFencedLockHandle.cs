using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Caching;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;

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

/// <summary>
/// Redis-backed <see cref="IDistributedLock"/> with lease expiry, owner-token
/// compare-and-delete release, and a monotonic fencing token per acquisition.
/// </summary>
/// <remarks>
/// Acquire is a single atomic Lua script (SET NX + INCR fence) and release only deletes when the
/// owner token still matches. There is no automatic renewal: choose an expiry
/// longer than the protected operation, or use <see cref="IFencedLockHandle.ExtendAsync"/>;
/// expiry during a hold is a real possibility the fencing token exists to defend against.
/// Lock and fence keys share a Redis Cluster hash tag. Redis failures propagate -- there is no
/// in-memory fallback. Mutual exclusion still assumes the protected resource honors fencing.
/// </remarks>
public sealed class RedisDistributedLock : IDistributedLock
{
    // KEYS[1] = lock key, KEYS[2] = fence key; ARGV[1] = owner token, ARGV[2] = lease ms, ARGV[3] = fence ttl ms
    private const string AcquireScript = """
        if redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2], 'NX') then
            local fence = redis.call('INCR', KEYS[2])
            redis.call('PEXPIRE', KEYS[2], ARGV[3])
            return fence
        end
        return false
        """;

    private const string ReleaseScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    private const string ExtendScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('PEXPIRE', KEYS[1], ARGV[2]) else return 0 end";

    private static readonly TimeSpan MinPollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisDistributedLockOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>Creates the lock over a shared multiplexer.</summary>
    public RedisDistributedLock(
        IConnectionMultiplexer connection,
        RedisDistributedLockOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _options = options is null ? new RedisDistributedLockOptions() : options;
        _options.Validate();
        _connection = connection;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private IDatabase Database => _connection.GetDatabase(_options.Database);

    /// <inheritdoc />
    /// <returns>An <see cref="IFencedLockHandle"/> on success, or null when the wait deadline elapsed.</returns>
    public async Task<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key.Length >= 256)
            throw new ArgumentOutOfRangeException(nameof(key), "Lock keys must stay bounded in size.");
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiry));
        if (waitTimeout is { } timeout && timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(waitTimeout));
        if (waitTimeout == Timeout.InfiniteTimeSpan)
            waitTimeout = null;

        var lockKey = (RedisKey)$"{_options.KeyPrefix}{{{key}}}";
        var fenceKey = (RedisKey)$"{_options.KeyPrefix}{{{key}}}:fence";
        var deadline = waitTimeout is { } wait
            ? _timeProvider.GetUtcNow() + wait
            : (DateTimeOffset?)null;
        var pollInterval = MinPollInterval;
        var fenceTtlMilliseconds = (long)Math.Ceiling(
            (expiry + _options.FenceRetention).TotalMilliseconds);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ownerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var result = await Database.ScriptEvaluateAsync(
                    AcquireScript,
                    [lockKey, fenceKey],
                    [ownerToken, (long)expiry.TotalMilliseconds, fenceTtlMilliseconds])
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsNull)
                return new LockHandle(this, key, lockKey, ownerToken, (long)result!);

            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining is { } left && left <= TimeSpan.Zero)
                return null;

            var delay = remaining is { } bounded && bounded < pollInterval ? bounded : pollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            pollInterval = pollInterval + pollInterval > MaxPollInterval
                ? MaxPollInterval
                : pollInterval + pollInterval;
        }
    }

    private async Task ReleaseAsync(RedisKey lockKey, string ownerToken)
    {
        await Database.ScriptEvaluateAsync(ReleaseScript, [lockKey], [ownerToken])
            .ConfigureAwait(false);
    }

    private async Task<bool> ExtendAsync(
        RedisKey lockKey,
        string ownerToken,
        TimeSpan expiry,
        CancellationToken cancellationToken)
    {
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiry));

        var extended = await Database.ScriptEvaluateAsync(
                ExtendScript,
                [lockKey],
                [ownerToken, (long)expiry.TotalMilliseconds])
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return (long)extended! == 1;
    }

    private sealed class LockHandle(
        RedisDistributedLock owner, string key, RedisKey lockKey, string ownerToken, long fencingToken)
        : IFencedLockHandle
    {
        private int _disposed;

        public string Key => key;

        public long FencingToken => fencingToken;

        public Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
            => owner.ExtendAsync(lockKey, ownerToken, expiry, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await owner.ReleaseAsync(lockKey, ownerToken).ConfigureAwait(false);
        }
    }
}
