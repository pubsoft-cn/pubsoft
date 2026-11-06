using System.Text.Json;

namespace PubSoft.Caching.Redis;

/// <summary>Redis cache settings.</summary>
public sealed class RedisCacheOptions
{
    /// <summary>Logical database index; -1 = multiplexer default.</summary>
    public int Database { get; set; } = -1;
    /// <summary>Prefix applied to all keys.</summary>
    public string KeyPrefix { get; set; } = string.Empty;
    /// <summary>Serialized value size cap in bytes -- an unbounded value can blow Redis memory on a single entry. Default 4 MiB.</summary>
    public int MaxValueBytes { get; set; } = 4_194_304;
    /// <summary>Value serializer settings.</summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}