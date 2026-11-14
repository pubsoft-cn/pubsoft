using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


/// <summary>Serializes captured property values for audit storage; non-scalar values are dropped.</summary>
public static class AuditValueSerializer
{
    /// <summary>Serializes a scalar value; returns null for complex types (captured as absent).</summary>
    [RequiresUnreferencedCode("Audit values are serialized by runtime type; the EF audit pipeline is not a trimming/AOT scenario.")]
    [RequiresDynamicCode("Audit values are serialized by runtime type; the EF audit pipeline is not a Native AOT scenario.")]
    public static JsonElement? Serialize(object? value, int maxStringLength)
    {
        if (value is null)
            return JsonSerializer.SerializeToElement<object?>(null);

        if (value is string text)
            value = text.Length > maxStringLength ? string.Concat(text.AsSpan(0, maxStringLength), "...") : text;

        var type = value.GetType();
        if (!(type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or DateTimeOffset or Guid or TimeSpan))
            return null;

        return JsonSerializer.SerializeToElement(value, type);
    }
}
