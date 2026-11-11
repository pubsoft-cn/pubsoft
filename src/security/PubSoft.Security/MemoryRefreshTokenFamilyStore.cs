namespace PubSoft.Security;

/// <summary>
/// Bounded in-process refresh-token family store. Suitable for single-instance deployments;
/// multi-instance deployments must swap in a shared store (e.g. Redis) behind the same contract.
/// </summary>
/// <param name="maxEntries">Hard cap on retained token records; 1-1_000_000.</param>
/// <param name="timeProvider">Clock; UTC real time when null.</param>
public sealed class MemoryRefreshTokenFamilyStore(int maxEntries = 100_000, TimeProvider? timeProvider = null) : IRefreshTokenFamilyStore
{
    private readonly Dictionary<string, RefreshTokenFamilyRecord> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _revokedFamilies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _subjectFamilies = new(StringComparer.Ordinal);
    private readonly List<string> _expiredTokenKeys = new();
    private readonly List<string> _expiredFamilyKeys = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public ValueTask<RefreshTokenFamilyRecord?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_tokens)
            return ValueTask.FromResult(_tokens.TryGetValue(tokenHash, out var record) ? record : null);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(RefreshTokenFamilyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_tokens)
        {
            if (!_tokens.TryGetValue(record.TokenHash, out _) && _tokens.Count >= maxEntries)
            {
                SweepExpired();
                if (_tokens.Count >= maxEntries)
                    throw new InvalidOperationException($"MemoryRefreshTokenFamilyStore is at capacity ({maxEntries}).");
            }

            _tokens[record.TokenHash] = record;
            if (!_subjectFamilies.TryGetValue(record.Subject, out var families))
            {
                families = new HashSet<string>(StringComparer.Ordinal);
                _subjectFamilies[record.Subject] = families;
            }

            families.Add(record.FamilyId);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<RefreshConsumeResult> TryConsumeAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_tokens)
        {
            if (!_tokens.TryGetValue(tokenHash, out var record))
                return ValueTask.FromResult(RefreshConsumeResult.AlreadyConsumed);
            if (_revokedFamilies.TryGetValue(record.FamilyId, out var until) && until > _timeProvider.GetUtcNow())
                return ValueTask.FromResult(RefreshConsumeResult.FamilyRevoked);
            if (record.Consumed)
                return ValueTask.FromResult(RefreshConsumeResult.AlreadyConsumed);

            _tokens[tokenHash] = record with { Consumed = true };
            return ValueTask.FromResult(RefreshConsumeResult.Consumed);
        }
    }

    /// <inheritdoc />
    public ValueTask RevokeFamilyAsync(string familyId, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            throw new ArgumentException("FamilyId must be non-empty.", nameof(familyId));

        lock (_tokens)
        {
            if (until > _timeProvider.GetUtcNow())
                _revokedFamilies[familyId] = until;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<int> RevokeSubjectAsync(string subject, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject must be non-empty.", nameof(subject));

        lock (_tokens)
        {
            if (!_subjectFamilies.Remove(subject, out var families))
                return ValueTask.FromResult(0);

            var now = _timeProvider.GetUtcNow();
            var revoked = 0;
            if (until > now)
            {
                foreach (var familyId in families)
                {
                    _revokedFamilies[familyId] = until;
                    revoked++;
                }
            }

            return ValueTask.FromResult(revoked);
        }
    }

    private void SweepExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _tokens)
        {
            if (pair.Value.TokenExpiresAt <= now)
                _expiredTokenKeys.Add(pair.Key);
        }

        foreach (var key in _expiredTokenKeys)
            _tokens.Remove(key);
        _expiredTokenKeys.Clear();

        foreach (var pair in _revokedFamilies)
        {
            if (pair.Value <= now)
                _expiredFamilyKeys.Add(pair.Key);
        }

        foreach (var key in _expiredFamilyKeys)
            _revokedFamilies.Remove(key);
        _expiredFamilyKeys.Clear();
    }
}
