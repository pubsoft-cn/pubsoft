using System.Globalization;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;

/// <summary>
/// Redis-backed <see cref="IRefreshTokenFamilyStore"/> for multi-instance deployments.
/// <para>
/// Key layout (cluster-safe via <c>{f:familyId}</c> hash tags):
/// <list type="bullet">
/// <item><c>{prefix}{f:familyId}:tok:{hash}</c> - token record hash.</item>
/// <item><c>{prefix}{f:familyId}:fam</c> - family revocation marker (same slot as its tokens).</item>
/// <item><c>{prefix}tokptr:{hash}</c> - lookup pointer from token hash to familyId.</item>
/// <item><c>{prefix}sub:{subject}</c> - subject -> familyIds index.</item>
/// </list>
/// The consume script runs on the token key and derives the same-slot marker key, so the
/// revoked-marker check and the consumed flip are one atomic step.
/// </para>
/// </summary>
public sealed class RedisRefreshTokenFamilyStore : IRefreshTokenFamilyStore
{
    private const int MaxIdLength = 64;

    // Saves one token record atomically (hash fields + absolute expiry) - single key.
    private const string SaveTokenScript = """
        redis.call('HSET', KEYS[1], 'fam', ARGV[1], 'sub', ARGV[2], 'iat', ARGV[3], 'texp', ARGV[4], 'fexp', ARGV[5], 'used', '0')
        redis.call('PEXPIREAT', KEYS[1], ARGV[4])
        """;

    // Indexes familyId under the subject and extends the index TTL monotonically - single key.
    private const string SaveSubjectIndexScript = """
        redis.call('SADD', KEYS[1], ARGV[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < tonumber(ARGV[2]) then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        """;

    // Atomic consume on the token key; derives the same-slot family marker key by swapping
    // the ':tok:<hash>' suffix for ':fam'. Returns 0 consumed, 1 already-consumed/missing,
    // 2 family-revoked.
    private const string ConsumeScript = """
        if not redis.call('EXISTS', KEYS[1]) == 1 then return 1 end
        local famkey = string.gsub(KEYS[1], ':tok:%w+$', ':fam')
        if redis.call('EXISTS', famkey) == 1 then return 2 end
        if redis.call('HGET', KEYS[1], 'used') == '1' then return 1 end
        redis.call('HSET', KEYS[1], 'used', '1')
        return 0
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly RedisRefreshTokenFamilyStoreOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the store over an existing Redis connection.</summary>
    /// <param name="redis">Shared connection multiplexer.</param>
    /// <param name="options">Bounded options; defaults when null.</param>
    /// <param name="timeProvider">Clock; UTC real time when null.</param>
    public RedisRefreshTokenFamilyStore(IConnectionMultiplexer redis, RedisRefreshTokenFamilyStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? new RedisRefreshTokenFamilyStoreOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<RefreshTokenFamilyRecord?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        CheckHash(tokenHash);
        var familyId = await ReadFamilyIdAsync(tokenHash).ConfigureAwait(false);
        if (familyId is null)
            return null;

        var entries = await Db().HashGetAllAsync(TokenKey(familyId, tokenHash)).ConfigureAwait(false);
        if (entries.Length == 0)
            return null;

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in entries)
            fields[entry.Name.ToString()] = entry.Value.ToString();

        return new RefreshTokenFamilyRecord(
            tokenHash,
            familyId,
            fields.GetValueOrDefault("sub") ?? string.Empty,
            FromMs(fields.GetValueOrDefault("iat")),
            FromMs(fields.GetValueOrDefault("texp")),
            FromMs(fields.GetValueOrDefault("fexp")),
            fields.GetValueOrDefault("used") == "1");
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(RefreshTokenFamilyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        CheckId(record.FamilyId, nameof(record));
        if (string.IsNullOrEmpty(record.Subject) || record.Subject.Length > _options.MaxSubjectLength)
            throw new ArgumentException("Subject must be non-empty and within MaxSubjectLength.", nameof(record));
        CheckHash(record.TokenHash);

        var db = Db();
        await db.ScriptEvaluateAsync(SaveTokenScript,
            keys: new RedisKey[] { TokenKey(record.FamilyId, record.TokenHash) },
            values: new RedisValue[]
            {
                record.FamilyId,
                record.Subject,
                ToMs(record.IssuedAt).ToString(CultureInfo.InvariantCulture),
                ToMs(record.TokenExpiresAt).ToString(CultureInfo.InvariantCulture),
                ToMs(record.FamilyExpiresAt).ToString(CultureInfo.InvariantCulture)
            }).ConfigureAwait(false);

        var pointerTtl = record.TokenExpiresAt - _timeProvider.GetUtcNow();
        if (pointerTtl > TimeSpan.Zero)
            await db.StringSetAsync(PointerKey(record.TokenHash), record.FamilyId, pointerTtl).ConfigureAwait(false);

        var subjectTtlMs = Math.Max(1, ToMs(record.FamilyExpiresAt) - ToMs(_timeProvider.GetUtcNow()));
        await db.ScriptEvaluateAsync(SaveSubjectIndexScript,
            keys: new RedisKey[] { SubjectKey(record.Subject) },
            values: new RedisValue[] { record.FamilyId, subjectTtlMs.ToString(CultureInfo.InvariantCulture) }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RefreshConsumeResult> TryConsumeAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        CheckHash(tokenHash);
        var familyId = await ReadFamilyIdAsync(tokenHash).ConfigureAwait(false);
        if (familyId is null)
            return RefreshConsumeResult.AlreadyConsumed;

        var result = (long)(await Db().ScriptEvaluateAsync(ConsumeScript,
            keys: new RedisKey[] { TokenKey(familyId, tokenHash) },
            values: Array.Empty<RedisValue>()).ConfigureAwait(false));

        return result switch
        {
            0 => RefreshConsumeResult.Consumed,
            1 => RefreshConsumeResult.AlreadyConsumed,
            _ => RefreshConsumeResult.FamilyRevoked
        };
    }

    /// <inheritdoc />
    public ValueTask RevokeFamilyAsync(string familyId, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        CheckId(familyId, nameof(familyId));
        var ttl = until - _timeProvider.GetUtcNow();
        return ttl <= TimeSpan.Zero
            ? ValueTask.CompletedTask
            : new ValueTask(Db().StringSetAsync(FamilyKey(familyId), "1", ttl));
    }

    /// <inheritdoc />
    public async ValueTask<int> RevokeSubjectAsync(string subject, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > _options.MaxSubjectLength)
            throw new ArgumentException("Subject must be non-empty and within MaxSubjectLength.", nameof(subject));

        var db = Db();
        var familyIds = await db.SetMembersAsync(SubjectKey(subject)).ConfigureAwait(false);
        var ttl = until - _timeProvider.GetUtcNow();
        if (familyIds.Length == 0 || ttl <= TimeSpan.Zero)
            return 0;

        var batch = db.CreateBatch();
        var tasks = new List<Task>(familyIds.Length + 1);
        foreach (var familyId in familyIds)
            tasks.Add(batch.StringSetAsync(FamilyKey(familyId.ToString()), "1", ttl));
        tasks.Add(batch.KeyDeleteAsync(SubjectKey(subject)));
        batch.Execute();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return familyIds.Length;
    }

    private async Task<string?> ReadFamilyIdAsync(string tokenHash)
    {
        var value = await Db().StringGetAsync(PointerKey(tokenHash)).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }

    private IDatabase Db() => _redis.GetDatabase(_options.Database);

    private string TokenKey(string familyId, string tokenHash) => _options.KeyPrefix + "{f:" + familyId + "}:tok:" + tokenHash;

    private string FamilyKey(string familyId) => _options.KeyPrefix + "{f:" + familyId + "}:fam";

    private string PointerKey(string tokenHash) => _options.KeyPrefix + "tokptr:" + tokenHash;

    private string SubjectKey(string subject) => _options.KeyPrefix + "sub:" + subject;

    private static void CheckHash(string tokenHash)
    {
        if (string.IsNullOrEmpty(tokenHash) || tokenHash.Length > 64)
            throw new ArgumentException("TokenHash must be non-empty and <=64 characters.", nameof(tokenHash));
    }

    private static void CheckId(string id, string paramName)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id.Contains(':', StringComparison.Ordinal) || id.Contains('}', StringComparison.Ordinal))
            throw new ArgumentException("Id must be non-empty, <=64 characters, and contain no ':' or '}'.", paramName);
    }

    private static long ToMs(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromMs(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.MinValue;
}
