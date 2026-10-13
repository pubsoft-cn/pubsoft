using System.Text.Json;

namespace PubSoft.Caching.Redis;

public sealed class RedisCacheOptions
{
    public int Database { get; set; } = -1;
    public string KeyPrefix { get; set; } = string.Empty;
    public JsonSerializerOptions SerializerOptions { get; set; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}