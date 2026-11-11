namespace PubSoft.Security;

/// <summary>
/// Bounded in-process token denylist. Single-instance deployments only - multi-instance
/// deployments register a shared implementation (e.g. Redis) behind the same contract.
/// </summary>
/// <param name="maxEntries">Hard cap on retained entries; 1-1_000_000.</param>
/// <param name="timeProvider">Clock; UTC real time when null.</param>
public sealed class MemoryTokenDenylist(int maxEntries = 100_000, TimeProvider? timeProvider = null) : ITokenDenylist
{
    private const int MaxTokenIdLength = 128;
    private const int MaxFieldLength = 256;

    private readonly Dictionary<string, TokenDenyEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _sweepBuffer = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public ValueTask DenyAsync(TokenDenyEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CheckId(entry.TokenId);
        if (entry.Subject is { Length: > MaxFieldLength } || entry.Reason is { Length: > MaxFieldLength })
            throw new ArgumentException("Subject and Reason must be <=256 characters.", nameof(entry));

        var now = _timeProvider.GetUtcNow();
        if (entry.ExpiresAt <= now)
            return ValueTask.CompletedTask;

        lock (_entries)
        {
            if (!_entries.TryGetValue(entry.TokenId, out _) && _entries.Count >= maxEntries)
            {
                SweepExpired(now);
                if (_entries.Count >= maxEntries)
                    throw new InvalidOperationException($"MemoryTokenDenylist is at capacity ({maxEntries}).");
            }

            _entries[entry.TokenId] = entry;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsDeniedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        CheckId(tokenId);
        var now = _timeProvider.GetUtcNow();
        lock (_entries)
        {
            if (!_entries.TryGetValue(tokenId, out var entry))
                return ValueTask.FromResult(false);
            if (entry.ExpiresAt <= now)
            {
                _entries.Remove(tokenId);
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(true);
        }
    }

    private void SweepExpired(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
                _sweepBuffer.Add(pair.Key);
        }

        foreach (var key in _sweepBuffer)
            _entries.Remove(key);
        _sweepBuffer.Clear();
    }

    private static void CheckId(string tokenId)
    {
        if (string.IsNullOrEmpty(tokenId) || tokenId.Length > MaxTokenIdLength)
            throw new ArgumentException("TokenId must be non-empty and <=128 characters.", nameof(tokenId));
    }
}
