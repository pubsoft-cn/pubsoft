using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Edge;


/// <summary>
/// Edge JSON options -- camelCase, no indentation, string enums. Shared by server and client.
/// </summary>
public static class EdgeJsonOptions
{
    /// <summary>Shared serializer options for the edge wire format (web defaults: camelCase, relaxed quoting).</summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };
}
