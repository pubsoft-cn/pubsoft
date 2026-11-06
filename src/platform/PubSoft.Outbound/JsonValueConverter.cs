using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace PubSoft.Outbound;

/// <summary>Converts JsonElement scalars to typed values.</summary>
public static class JsonValueConverter
{
    /// <summary>Converts a JSON element to the target type.</summary>
    [RequiresUnreferencedCode("Runtime-typed JSON conversion uses reflection-based serialization metadata.")]
    [RequiresDynamicCode("Runtime-typed JSON conversion requires dynamic serialization code.")]
    public static object? Convert(JsonElement element, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(JsonElement))
            return element.Clone();
        if (underlying == typeof(string))
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        if (underlying == typeof(bool))
            return element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.GetBoolean()
                : bool.Parse(ReadScalarText(element));
        if (underlying == typeof(Guid))
            return Guid.Parse(ReadScalarText(element));
        if (underlying == typeof(DateOnly))
            return DateOnly.Parse(ReadScalarText(element), CultureInfo.InvariantCulture);
        if (underlying == typeof(DateTime))
            return DateTime.Parse(ReadScalarText(element), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (underlying == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(ReadScalarText(element), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (underlying.IsEnum)
        {
            var text = ReadScalarText(element);
            return Enum.TryParse(underlying, text, ignoreCase: true, out var parsed)
                ? parsed
                : Enum.ToObject(underlying, System.Convert.ChangeType(text, Enum.GetUnderlyingType(underlying), CultureInfo.InvariantCulture)!);
        }
        if (underlying == typeof(byte))
            return byte.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(sbyte))
            return sbyte.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(short))
            return short.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(ushort))
            return ushort.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(int))
            return int.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(uint))
            return uint.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(long))
            return long.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(ulong))
            return ulong.Parse(ReadScalarText(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (underlying == typeof(decimal))
            return decimal.Parse(ReadScalarText(element), NumberStyles.Number, CultureInfo.InvariantCulture);
        if (underlying == typeof(double))
            return double.Parse(ReadScalarText(element), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (underlying == typeof(float))
            return float.Parse(ReadScalarText(element), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (underlying == typeof(object))
            return element.GetRawText();

        return JsonSerializer.Deserialize(element.GetRawText(), targetType);
    }

    /// <summary>Reads the first matching string field.</summary>
    public static string? GetString(JsonElement root, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    /// <summary>Reads an amount in minor units.</summary>
    public static long? GetAmountFen(JsonElement root, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                return number;
            if (value.ValueKind == JsonValueKind.String
                && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return number;
        }
        return null;
    }

    /// <summary>Reads the raw JSON of the first matching field.</summary>
    public static string? GetRawJson(JsonElement root, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
                && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                return value.GetRawText();
        return null;
    }

    /// <summary>Parses a JSON string to an element; null when absent.</summary>
    public static JsonElement? ParseRoot(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            return null;
        using var document = JsonDocument.Parse(resultJson);
        return document.RootElement.Clone();
    }

    private static string ReadScalarText(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
}