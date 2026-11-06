using System.Collections.Concurrent;

namespace PubSoft.Caching;

/// <summary>
/// Single-process <see cref="IDistributedLock"/> backed by keyed semaphores. Suitable for
/// development, tests, and hosts that run exactly one instance; it provides no cross-process
/// mutual exclusion and no fencing tokens -- hosts needing either must supply a distributed
/// implementation.
/// </summary>
/// <remarks>
/// Expiry is a safety net enforced on competing acquisitions: a hold past its expiry
/// may be force-released when another caller contends for the same key. The stale holder is not
/// notified, matching lease-expiry semantics of distributed locks. The entry table is bounded;
/// when capacity is exhausted acquisition fails closed (returns <c>null</c>).
/// </remarks>
/// <summary>Single-process lock - dev/single-instance only, no cross-process exclusion or fencing.</summary>
public sealed class InProcessDistributedLock : IDistributedLock, IDisposable
{
    private static readonly TimeSpan PollSlice = TimeSpan.FromMilliseconds(50);

    private readonly ConcurrentDictionary<string, LockEntry> _entries = new(StringComparer.Ordinal);
    /// <summary>Insertion gate for new entries -- capacity check and TryAdd must be atomic or a full table can be overrun concurrently.</summary>
    private readonly object _entryGate = new();
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly int _maxEntries;
    private long _nextToken;
    private int _disposed;

    /// <summary>Creates the lock table.</summary>
    public InProcessDistributedLock(TimeProvider? timeProvider = null, int maxEntries = 4096)
    {
        if (maxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxEntries = maxEntries;
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan expiry,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiry));
        if (waitTimeout is { } timeout && timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(waitTimeout));
        if (waitTimeout == Timeout.InfiniteTimeSpan)
            waitTimeout = null;
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var deadline = waitTimeout is { } wait
            ? _timeProvider.GetUtcNow() + wait
            : (DateTimeOffset?)null;

        while (true)
        {
            var entry = GetOrCreateEntry(key);
            if (entry is null)
                return null;

            lock (entry.Sync)
            {
                if (entry.Removed)
                    continue;

                entry.WaiterCount++;
            }

            try
            {
                while (true)
                {
                    var now = _timeProvider.GetUtcNow();
                    ForceReleaseIfExpired(entry, now);

                    var remaining = deadline - now;
                    var slice = remaining is { } bounded && bounded < PollSlice ? bounded : PollSlice;
                    if (slice < TimeSpan.Zero)
                        slice = TimeSpan.Zero;

                    if (await entry.Semaphore.WaitAsync(slice, cancellationToken).ConfigureAwait(false))
                    {
                        var token = Interlocked.Increment(ref _nextToken);
                        lock (entry.Sync)
                        {
                            // WaiterCount > 0 prevented eviction; the entry cannot be Removed here.
                            entry.HolderToken = token;
                            entry.ExpiryUtc = _timeProvider.GetUtcNow() + expiry;
                        }
                        return new Releaser(this, entry, token);
                    }

                    if (remaining is { } boundedRemaining && boundedRemaining <= TimeSpan.Zero)
                        return null;
                }
            }
            finally
            {
                lock (entry.Sync)
                    entry.WaiterCount--;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private LockEntry? GetOrCreateEntry(string key)
    {
        if (_entries.TryGetValue(key, out var existing))
            return existing;

        // 命中未中才进 gate--容量检查 + TryAdd 原子化
        lock (_entryGate)
        {
            if (_entries.TryGetValue(key, out existing))
                return existing;
            if (_entries.Count >= _maxEntries)
                EvictIdleEntries();
            if (_entries.Count >= _maxEntries)
                return null;

            var entry = new LockEntry();
            return _entries.TryAdd(key, entry) ? entry : null;
        }
    }

    private void EvictIdleEntries()
    {
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            lock (entry.Sync)
            {
                if (entry.Removed || entry.HolderToken != 0 || entry.WaiterCount != 0 || entry.Semaphore.CurrentCount != 1)
                    continue;

                if (_entries.TryRemove(pair))
                    entry.Removed = true;
            }
        }
    }

    private void ForceReleaseIfExpired(LockEntry entry, DateTimeOffset now)
    {
        if (entry.ExpiryUtc > now)
            return;

        lock (entry.Sync)
        {
            if (entry.HolderToken == 0 || entry.ExpiryUtc > now)
                return;

            // The expired holder's own Release becomes a no-op via the token check.
            entry.HolderToken = 0;
            entry.Semaphore.Release();
        }
    }

    private void Release(LockEntry entry, long token)
    {
        lock (entry.Sync)
        {
            if (entry.HolderToken != token)
                return;

            entry.HolderToken = 0;
            entry.Semaphore.Release();
        }
    }

    private sealed class LockEntry
    {
        public readonly object Sync = new();
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public long HolderToken;
        public DateTimeOffset ExpiryUtc;
        public int WaiterCount;
        public bool Removed;
    }

    private sealed class Releaser(InProcessDistributedLock owner, LockEntry entry, long token) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release(entry, token);

            return ValueTask.CompletedTask;
        }
    }
}
