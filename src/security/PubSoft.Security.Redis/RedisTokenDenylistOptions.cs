namespace PubSoft.Security.Redis;

/// <summary>Bounded options for the Redis token denylist.</summary>
public sealed class RedisTokenDenylistOptions
{
    /// <summary>Key prefix for all deny entries; non-empty and &lt;=64 chars.</summary>
    public string KeyPrefix { get; set; } = "pubsoft:deny:";

    /// <summary>Redis logical database index; -1 uses the connection default.</summary>
    public int Database { get; set; } = -1;

    /// <summary>Maximum accepted token id length; 1-128.</summary>
    public int MaxTokenIdLength { get; set; } = 128;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix) || KeyPrefix.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(KeyPrefix), KeyPrefix, "KeyPrefix must be non-empty and <=64 characters.");
        if (Database < -1)
            throw new ArgumentOutOfRangeException(nameof(Database), Database, "Database must be -1 or a valid Redis database index.");
        if (MaxTokenIdLength is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(MaxTokenIdLength), MaxTokenIdLength, "MaxTokenIdLength must be between 1 and 128.");
    }
}
