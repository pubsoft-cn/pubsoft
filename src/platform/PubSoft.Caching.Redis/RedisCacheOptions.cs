using System.Text.Json;

namespace PubSoft.Caching.Redis;

public sealed class RedisCacheOptions
{
    public int Database { get; set; } = -1;
    public string KeyPrefix { get; set; } = string.Empty;
    /// <summary>序列化后 value 大小上限（字节）——无上限会让单条缓存把 Redis 内存打爆。默认 4 MiB。</summary>
    public int MaxValueBytes { get; set; } = 4_194_304;
    public JsonSerializerOptions SerializerOptions { get; set; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}