using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;


/// <summary>Atomically reserves HMAC nonces across application instances using Redis SET NX with expiry.</summary>
public sealed class RedisNonceStore : INonceStore
{
    private const string ReserveScript = """
        local redis_time = redis.call('TIME')
        local now_ms = math.ceil(tonumber(redis_time[1]) * 1000 + tonumber(redis_time[2]) / 1000)
        local ttl_ms = tonumber(ARGV[1]) - now_ms
        if ttl_ms <= 0 then
            return 0
        end
        if redis.call('SET', KEYS[1], '1', 'PX', ttl_ms, 'NX') then
            return 1
        end
        return 0
        """;

    private readonly IDatabase _database;
    private readonly RedisNonceStoreOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>Creates the store over a shared multiplexer.</summary>
    public RedisNonceStore(
        IConnectionMultiplexer connection,
        RedisNonceStoreOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _options = (options ?? new RedisNonceStoreOptions()).CloneAndValidate();
        _database = connection.GetDatabase(_options.Database);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryUseAsync(
        string scope,
        string nonce,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        if (scope.Length > _options.MaxScopeLength)
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (nonce.Length > _options.MaxNonceLength)
            throw new ArgumentOutOfRangeException(nameof(nonce));
        cancellationToken.ThrowIfCancellationRequested();

        if (expiresAt <= _timeProvider.GetUtcNow())
            return false;

        var key = BuildKey(scope, nonce);
        var reserve = _database.ScriptEvaluateAsync(
            ReserveScript,
            [key],
            [expiresAt.ToUnixTimeMilliseconds()]);
        var result = await reserve.WaitAsync(cancellationToken).ConfigureAwait(false);
        return (long)result == 1;
    }

    private RedisKey BuildKey(string scope, string nonce)
    {
        var scopeBytes = Encoding.UTF8.GetBytes(scope);
        var nonceBytes = Encoding.UTF8.GetBytes(nonce);
        var input = new byte[sizeof(int) + scopeBytes.Length + sizeof(int) + nonceBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(0, sizeof(int)), scopeBytes.Length);
        scopeBytes.CopyTo(input.AsSpan(sizeof(int)));
        var nonceLengthOffset = sizeof(int) + scopeBytes.Length;
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(nonceLengthOffset, sizeof(int)), nonceBytes.Length);
        nonceBytes.CopyTo(input.AsSpan(nonceLengthOffset + sizeof(int)));
        var digest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(scopeBytes);
        CryptographicOperations.ZeroMemory(nonceBytes);
        CryptographicOperations.ZeroMemory(input);
        return _options.KeyPrefix + digest;
    }
}
