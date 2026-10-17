using System.Text;
using System.Text.Json;
using PubSoft.Caching;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;

public sealed class RedisCache : ICache
{
    private const int DeleteBatchSize = 500;
    private readonly IConnectionMultiplexer _connection;
    private readonly int _database;
    private readonly string _keyPrefix;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly int _maxValueBytes;

    public RedisCache(IConnectionMultiplexer connection, RedisCacheOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new RedisCacheOptions();
        if (options.Database < -1)
            throw new ArgumentOutOfRangeException(nameof(options.Database));
        ArgumentNullException.ThrowIfNull(options.KeyPrefix);
        if (options.KeyPrefix.Length >= 512)
            throw new ArgumentOutOfRangeException(nameof(options.KeyPrefix), "Key prefix must leave room for a cache key.");
        ArgumentNullException.ThrowIfNull(options.SerializerOptions);

        _connection = connection;
        _database = options.Database;
        _keyPrefix = options.KeyPrefix;
        _serializerOptions = new JsonSerializerOptions(options.SerializerOptions);
        if (options.MaxValueBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(options.MaxValueBytes), "MaxValueBytes must be at least 1024.");
        _maxValueBytes = options.MaxValueBytes;
    }

    private IDatabase Database => _connection.GetDatabase(_database);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        var value = await Database.StringGetAsync(PhysicalKey(key))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString(), _serializerOptions) : null;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        ValidateValue(value);
        ValidateExpiry(expiry);
        var json = JsonSerializer.Serialize(value, _serializerOptions);
        if (Encoding.UTF8.GetByteCount(json) > _maxValueBytes)
            throw new ArgumentOutOfRangeException(nameof(value), $"Serialized cache value exceeds {_maxValueBytes} bytes.");
        await Database.StringSetAsync(PhysicalKey(key), json, ToExpiration(expiry))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Expiration ToExpiration(TimeSpan? expiry)
        => expiry.HasValue ? new Expiration(expiry.Value) : Expiration.Default;

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        await Database.KeyDeleteAsync(PhysicalKey(key))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        return await Database.KeyExistsAsync(PhysicalKey(key))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        ValidateExpiry(expiry);

        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;

        cancellationToken.ThrowIfCancellationRequested();
        var value = await factory().ConfigureAwait(false);
        if (value is not null)
            await SetAsync(key, value, expiry, cancellationToken).ConfigureAwait(false);
        return value!;
    }

    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        var keyList = keys.ToArray();
        foreach (var key in keyList) ValidateKey(key);

        var result = new Dictionary<string, T?>(StringComparer.Ordinal);
        if (keyList.Length == 0) return result;

        var values = new RedisValue[keyList.Length];
        for (var offset = 0; offset < keyList.Length; offset += DeleteBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(DeleteBatchSize, keyList.Length - offset);
            var reads = new Task<RedisValue>[count];
            for (var index = 0; index < count; index++)
                reads[index] = Database.StringGetAsync(PhysicalKey(keyList[offset + index]));
            var chunk = await Task.WhenAll(reads).WaitAsync(cancellationToken).ConfigureAwait(false);
            Array.Copy(chunk, 0, values, offset, count);
        }
        for (var index = 0; index < keyList.Length; index++)
        {
            result[keyList[index]] = values[index].HasValue
                ? JsonSerializer.Deserialize<T>(values[index].ToString(), _serializerOptions)
                : null;
        }
        return result;
    }

    public async Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateExpiry(expiry);

        var serializedItems = items.Select(item =>
        {
            ValidateKey(item.Key);
            ValidateValue(item.Value);
            return (Key: PhysicalKey(item.Key), Json: JsonSerializer.Serialize(item.Value, _serializerOptions));
        }).ToArray();
        if (serializedItems.Length == 0) return;

        for (var offset = 0; offset < serializedItems.Length; offset += DeleteBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(DeleteBatchSize, serializedItems.Length - offset);
            var writes = new Task<bool>[count];
            for (var index = 0; index < count; index++)
            {
                var item = serializedItems[offset + index];
                writes[index] = Database.StringSetAsync(item.Key, item.Json, ToExpiration(expiry));
            }
            await Task.WhenAll(writes).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();
        var keyList = keys.ToArray();
        foreach (var key in keyList) ValidateKey(key);
        if (keyList.Length == 0) return;

        var distinctKeys = keyList.Distinct(StringComparer.Ordinal).ToArray();
        for (var offset = 0; offset < distinctKeys.Length; offset += DeleteBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(DeleteBatchSize, distinctKeys.Length - offset);
            var deletes = new Task<bool>[count];
            for (var index = 0; index < count; index++)
                deletes[index] = Database.KeyDeleteAsync(PhysicalKey(distinctKeys[offset + index]));
            await Task.WhenAll(deletes).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePattern(pattern);
        var redisPattern = ToRedisPattern(pattern);
        var database = Database;

        foreach (var endpoint in _connection.GetEndPoints().Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = _connection.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;

            var keys = new List<RedisKey>(DeleteBatchSize);
            await foreach (var key in server.KeysAsync(
                    database: _database,
                    pattern: redisPattern,
                    pageSize: DeleteBatchSize)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                keys.Add(key);
                if (keys.Count < DeleteBatchSize) continue;
                await DeleteKeysAsync(database, keys, cancellationToken).ConfigureAwait(false);
                keys.Clear();
            }

            if (keys.Count > 0)
                await DeleteKeysAsync(database, keys, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeleteKeysAsync(IDatabase database, List<RedisKey> keys, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deletes = keys.Select(key => database.KeyDeleteAsync(key)).ToArray();
        await Task.WhenAll(deletes).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private RedisKey PhysicalKey(string key) => _keyPrefix + key;

    private string ToRedisPattern(string pattern)
    {
        var builder = new StringBuilder(_keyPrefix.Length + pattern.Length);
        foreach (var character in _keyPrefix) AppendEscaped(builder, character);
        foreach (var character in pattern)
        {
            if (character is '*' or '?') builder.Append(character);
            else AppendEscaped(builder, character);
        }
        return builder.ToString();
    }

    private static void AppendEscaped(StringBuilder builder, char character)
    {
        if (character is '\\' or '*' or '?' or '[' or ']') builder.Append('\\');
        builder.Append(character);
    }

    private void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key must not be empty.", nameof(key));
        if (_keyPrefix.Length + key.Length > 512)
            throw new ArgumentException("Prefixed cache key must not exceed 512 characters.", nameof(key));
    }

    private static void ValidateValue<T>(T value) where T : class
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
    }

    private static void ValidateExpiry(TimeSpan? expiry)
    {
        if (expiry.HasValue && expiry.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expiry), "Expiry must be positive when specified.");
    }

    private static void ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        if (pattern.Length > 512)
            throw new ArgumentException("Pattern must not exceed 512 characters.", nameof(pattern));
    }
}