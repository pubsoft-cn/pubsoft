namespace PubSoft.Caching.Redis;


/// <summary>Redis lock settings.</summary>
public sealed class RedisDistributedLockOptions
{
    /// <summary>Redis logical database index; -1 uses the multiplexer default.</summary>
    public int Database { get; set; } = -1;

    /// <summary>Prefix applied to all lock keys.</summary>
    public string KeyPrefix { get; set; } = "pubsoft:lock:";

    /// <summary>How long a fencing counter outlives its lock lease.</summary>
    public TimeSpan FenceRetention { get; set; } = TimeSpan.FromHours(1);

    internal void Validate()
    {
        if (Database < -1)
            throw new ArgumentOutOfRangeException(nameof(Database));
        ArgumentNullException.ThrowIfNull(KeyPrefix);
        if (KeyPrefix.Length >= 480)
            throw new ArgumentOutOfRangeException(nameof(KeyPrefix), "Key prefix must leave room for a lock key.");
        if (FenceRetention <= TimeSpan.Zero || FenceRetention > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(FenceRetention));
    }
}
