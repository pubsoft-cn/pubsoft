namespace PubSoft.Security.Redis;

/// <summary>Redis nonce-store settings.</summary>
public sealed class RedisNonceStoreOptions
{
    /// <summary>Logical database index; -1 = multiplexer default.</summary>
    public int Database { get; set; } = -1;
    /// <summary>Prefix for nonce keys.</summary>
    public string KeyPrefix { get; set; } = "pubsoft:security:nonce:";
    /// <summary>Scope string cap.</summary>
    public int MaxScopeLength { get; set; } = 512;
    /// <summary>Nonce string cap.</summary>
    public int MaxNonceLength { get; set; } = 1024;

    internal RedisNonceStoreOptions CloneAndValidate()
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix) || KeyPrefix.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(KeyPrefix));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxScopeLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNonceLength);
        if (Database < -1)
            throw new ArgumentOutOfRangeException(nameof(Database));

        return new RedisNonceStoreOptions
        {
            Database = Database,
            KeyPrefix = KeyPrefix,
            MaxScopeLength = MaxScopeLength,
            MaxNonceLength = MaxNonceLength
        };
    }
}
