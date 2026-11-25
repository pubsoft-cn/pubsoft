using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Scheduling;

/// <summary>
/// Reads any scalar JSON token as a string. Configuration materialized through
/// IConfiguration may re-type string values as numbers or booleans; this converter
/// lets string-typed members accept those tokens instead of failing the payload.
/// </summary>
public sealed class ScalarStringJsonConverter : JsonConverter<string>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False =>
                JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            _ => throw new JsonException($"Expected a scalar JSON token, got {reader.TokenType}.")
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    /// <inheritdoc />
    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString()!;

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WritePropertyName(value);
}
