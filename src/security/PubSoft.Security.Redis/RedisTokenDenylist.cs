using System.Globalization;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;

/// <summary>
/// Redis-backed <see cref="ITokenDenylist"/> for multi-instance deployments. Each entry is a
/// single hash key expiring at the denied token's own expiry - the denylist is the shared
/// revocation source of truth; every validator instance must use the same prefix + database.
/// </summary>
public sealed class RedisTokenDenylist : ITokenDenylist
{
    private const int MaxFieldLength = 256;

    // Writes one deny entry atomically (fields + absolute expiry) - single key.
    private const string DenyScript = """
        redis.call('HSET', KEYS[1], 'sub', ARGV[1], 'rsn', ARGV[2], 'iat', ARGV[3])
        redis.call('PEXPIREAT', KEYS[1], ARGV[4])
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly RedisTokenDenylistOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the denylist over an existing Redis connection.</summary>
    /// <param name="redis">Shared connection multiplexer.</param>
    /// <param name="options">Bounded options; defaults when null.</param>
    /// <param name="timeProvider">Clock; UTC real time when null.</param>
    public RedisTokenDenylist(IConnectionMultiplexer redis, RedisTokenDenylistOptions? options = null, TimeProvider? timeProvider = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? new RedisTokenDenylistOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask DenyAsync(TokenDenyEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CheckId(entry.TokenId);
        if (entry.Subject is { Length: > MaxFieldLength } || entry.Reason is { Length: > MaxFieldLength })
            throw new ArgumentException("Subject and Reason must be <=256 characters.", nameof(entry));
        if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
            return ValueTask.CompletedTask;

        return new ValueTask(Db().ScriptEvaluateAsync(DenyScript,
            keys: new RedisKey[] { EntryKey(entry.TokenId) },
            values: new RedisValue[]
            {
                entry.Subject ?? string.Empty,
                entry.Reason ?? string.Empty,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                entry.ExpiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            }));
    }

    /// <inheritdoc />
    public ValueTask<bool> IsDeniedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        CheckId(tokenId);
        return new ValueTask<bool>(Db().KeyExistsAsync(EntryKey(tokenId)));
    }

    private IDatabase Db() => _redis.GetDatabase(_options.Database);

    private string EntryKey(string tokenId) => _options.KeyPrefix + tokenId;

    private void CheckId(string tokenId)
    {
        if (string.IsNullOrEmpty(tokenId) || tokenId.Length > _options.MaxTokenIdLength)
            throw new ArgumentException("TokenId must be non-empty and within MaxTokenIdLength.", nameof(tokenId));
    }
}
