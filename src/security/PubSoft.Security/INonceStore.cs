namespace PubSoft.Security;

/// <summary>Atomically reserves a nonce until its signed timestamp can no longer be accepted.</summary>
public interface INonceStore
{
    ValueTask<bool> TryUseAsync(
        string scope,
        string nonce,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}

/// <summary>A bounded, process-local nonce store. It does not provide multi-instance replay protection.</summary>
public sealed class MemoryNonceStore : INonceStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Scope, string Nonce), DateTimeOffset> _entries = [];
    private readonly int _maxEntries;
    private readonly TimeProvider _timeProvider;

    public MemoryNonceStore(int maxEntries = 100_000, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<bool> TryUseAsync(
        string scope,
        string nonce,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        if (expiresAt <= now)
            return ValueTask.FromResult(false);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var expired in _entries.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                _entries.Remove(expired);

            var key = (scope, nonce);
            if (_entries.TryGetValue(key, out var currentExpiry) && currentExpiry > now)
                return ValueTask.FromResult(false);

            if (_entries.Count >= _maxEntries)
                return ValueTask.FromResult(false);

            _entries[key] = expiresAt;
            return ValueTask.FromResult(true);
        }
    }
}