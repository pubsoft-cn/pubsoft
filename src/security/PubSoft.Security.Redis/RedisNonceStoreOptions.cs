using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;

public sealed class RedisNonceStoreOptions
{
    public int Database { get; set; } = -1;
    public string KeyPrefix { get; set; } = "pubsoft:security:nonce:";
    public int MaxScopeLength { get; set; } = 512;
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
