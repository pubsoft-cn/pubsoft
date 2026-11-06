using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace PubSoft.Caching;

public sealed class MemoryCache : ICache, IDisposable
{
    /// <summary>单值上限——字符串 4 Mi 字符 / 二进制 4 MiB，防止单条缓存耗尽内存。</summary>
    private const int MaxValueChars = 4_194_304;
    private const int MaxValueBytes = 4_194_304;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _factoryTasks = new(StringComparer.Ordinal);
    private readonly Timer _cleanupTimer;
    private readonly int _maxSize;
    private readonly object _lock = new();
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);
    private bool _disposed;

    public MemoryCache(TimeSpan? cleanupInterval = null, int maxSize = 10000, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var interval = cleanupInterval ?? TimeSpan.FromMinutes(5);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(cleanupInterval), "Cleanup interval must be positive.");
        if (maxSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSize), "Maximum cache size must be at least one.");

        _maxSize = maxSize;
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, interval, interval);
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired(_timeProvider.GetUtcNow().UtcDateTime))
        {
            entry.LastAccessTime = _timeProvider.GetUtcNow().UtcDateTime;
            return Task.FromResult(entry.Value as T);
        }
        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        ValidateValue(value);
        ValidateExpiry(expiry);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        lock (_lock)
        {
            ThrowIfDisposed();
            if (!_cache.ContainsKey(key) && _cache.Count >= _maxSize) // pubsoft:exempt PUBSOFT012 "ContainsKey guards eviction only — set always writes"
                EvictLRUEntries();
            _cache[key] = new CacheEntry(value, now, expiry.HasValue ? now.Add(expiry.Value) : null, now);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        return Task.FromResult(_cache.TryGetValue(key, out var entry) && !entry.IsExpired(_timeProvider.GetUtcNow().UtcDateTime));
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ValidateKey(key);
        ValidateExpiry(expiry);
        var cachedValue = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cachedValue is not null) return cachedValue;

        cancellationToken.ThrowIfCancellationRequested();
        var factoryTask = _factoryTasks.GetOrAdd(key, _ => new Lazy<Task<object?>>(
            async () => await factory().ConfigureAwait(false),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var value = (T?)await factoryTask.Value.ConfigureAwait(false);
            if (value is not null)
                await SetAsync(key, value, expiry, cancellationToken).ConfigureAwait(false);
            return value!;
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<object?>>>>)_factoryTasks)
                .Remove(new KeyValuePair<string, Lazy<Task<object?>>>(key, factoryTask));
        }
    }

    public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<string, T?>(StringComparer.Ordinal);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var key in keys)
        {
            ValidateKey(key);
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired(_timeProvider.GetUtcNow().UtcDateTime))
            {
                entry.LastAccessTime = now;
                result[key] = entry.Value as T;
            }
            else
            {
                result[key] = null;
            }
        }
        return Task.FromResult(result);
    }

    public Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateExpiry(expiry);
        var materialized = items.ToArray();
        foreach (var item in materialized)
        {
            ValidateKey(item.Key);
            ValidateValue(item.Value);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime? expiryTime = expiry.HasValue ? now.Add(expiry.Value) : null;
        lock (_lock)
        {
            ThrowIfDisposed();
            foreach (var item in materialized)
            {
                if (!_cache.ContainsKey(item.Key) && _cache.Count >= _maxSize) // pubsoft:exempt PUBSOFT012 "ContainsKey guards eviction only — set always writes"
                    EvictLRUEntries();
                _cache[item.Key] = new CacheEntry(item.Value, now, expiryTime, now);
            }
        }
        return Task.CompletedTask;
    }

    public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in keys)
        {
            ValidateKey(key);
            _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePattern(pattern);
        var regex = _regexCache.GetOrAdd(pattern, static value =>
        {
            var builder = new StringBuilder("^");
            foreach (var character in value)
            {
                builder.Append(character switch
                {
                    '*' => ".*",
                    '?' => ".",
                    _ => Regex.Escape(character.ToString()),
                });
            }
            builder.Append('$');
            var regexPattern = builder.ToString();
            return new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        });
        foreach (var key in _cache.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (regex.IsMatch(key))
                _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public CacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                ExpiredEntries = _cache.Values.Count(entry => entry.IsExpired(_timeProvider.GetUtcNow().UtcDateTime)),
                SizeLimit = _maxSize,
            };
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupTimer.Dispose();
            _cache.Clear();
            _regexCache.Clear();
        }
    }

    private void CleanupExpiredEntries(object? state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var pair in _cache)
            {
                if (pair.Value.IsExpired(_timeProvider.GetUtcNow().UtcDateTime))
                    _cache.TryRemove(pair.Key, out _);
            }
        }
    }

    private void EvictLRUEntries()
    {
        var evictCount = Math.Max(1, _maxSize / 10);
        var comparer = Comparer<CacheEntry>.Create((left, right) =>
        {
            var comparison = left.LastAccessTime.CompareTo(right.LastAccessTime);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.Key, right.Key);
        });
        var heap = new SortedSet<CacheEntry>(comparer);
        foreach (var pair in _cache)
        {
            var entry = pair.Value;
            entry.Key = pair.Key;
            if (heap.Count < evictCount)
                heap.Add(entry);
            else if (comparer.Compare(entry, heap.Max!) < 0)
            {
                heap.Remove(heap.Max!);
                heap.Add(entry);
            }
        }
        foreach (var entry in heap)
            _cache.TryRemove(entry.Key, out _);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key must not be empty.", nameof(key));
        if (key.Length > 512)
            throw new ArgumentException("Cache key must not exceed 512 characters.", nameof(key));
    }

    private static void ValidateValue<T>(T value) where T : class
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        // 内存缓存同样限值上限——string/byte[] 可直接度量；其余托管对象由 _maxSize 条目数封顶兜底
        switch (value)
        {
            case string s when s.Length > MaxValueChars:
                throw new ArgumentOutOfRangeException(nameof(value), $"String cache value exceeds {MaxValueChars} characters.");
            case byte[] b when b.Length > MaxValueBytes:
                throw new ArgumentOutOfRangeException(nameof(value), $"Binary cache value exceeds {MaxValueBytes} bytes.");
        }
    }

    private static void ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
    }

    private static void ValidateExpiry(TimeSpan? expiry)
    {
        if (expiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expiry), "Expiry must be positive when specified.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public sealed class CacheStatistics
    {
        public int TotalEntries { get; init; }
        public int ExpiredEntries { get; init; }
        public int SizeLimit { get; init; }
    }

    private sealed class CacheEntry(object value, DateTime createdTime, DateTime? expiryTime, DateTime lastAccessTime)
    {
        public object Value { get; } = value;
        public DateTime CreatedTime { get; } = createdTime;
        public DateTime? ExpiryTime { get; } = expiryTime;
        public DateTime LastAccessTime { get; set; } = lastAccessTime;
        public string Key { get; set; } = "";
        public bool IsExpired(DateTime now) => ExpiryTime.HasValue && now >= ExpiryTime.Value;
    }
}
