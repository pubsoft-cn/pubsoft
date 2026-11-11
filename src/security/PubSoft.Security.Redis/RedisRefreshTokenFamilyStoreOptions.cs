namespace PubSoft.Security.Redis;

/// <summary>Bounded options for the Redis refresh-token family store.</summary>
public sealed class RedisRefreshTokenFamilyStoreOptions
{
    /// <summary>Key prefix for all family entries; non-empty and &lt;=64 chars.</summary>
    public string KeyPrefix { get; set; } = "pubsoft:rtf:";

    /// <summary>Redis logical database index; -1 uses the connection default.</summary>
    public int Database { get; set; } = -1;

    /// <summary>Maximum accepted subject length; 1-1024.</summary>
    public int MaxSubjectLength { get; set; } = 256;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix) || KeyPrefix.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(KeyPrefix), KeyPrefix, "KeyPrefix must be non-empty and <=64 characters.");
        if (Database < -1)
            throw new ArgumentOutOfRangeException(nameof(Database), Database, "Database must be -1 or a valid Redis database index.");
        if (MaxSubjectLength is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxSubjectLength), MaxSubjectLength, "MaxSubjectLength must be between 1 and 1024.");
    }
}
