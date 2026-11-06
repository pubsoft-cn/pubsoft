using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Maps flat field dictionaries to contract types.</summary>
[RequiresUnreferencedCode("Response mapping uses reflection over response members; preserve contract types under trimming.")]
[RequiresDynamicCode("Response mapping deserializes nested values at runtime.")]
public static class DictionaryResponseMapper
{
    /// <summary>Maps fields to <typeparamref name="T"/>.</summary>
    public static T Map<T>(IReadOnlyDictionary<string, string> fields, INamingPolicy namingPolicy) where T : new()
        => (T)Map(typeof(T), fields, namingPolicy);

    /// <summary>Maps fields to the given type.</summary>
    public static object Map(Type responseType, IReadOnlyDictionary<string, string> fields, INamingPolicy namingPolicy)
    {
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(namingPolicy);
        return MapCore(responseType, fields, namingPolicy, 0);
    }

    private static object MapCore(Type responseType, IReadOnlyDictionary<string, string> fields, INamingPolicy namingPolicy, int depth)
    {
        if (depth > 64)
            throw new InvalidOperationException("Flattened response nesting exceeds the maximum supported depth.");
        var instance = Activator.CreateInstance(responseType)
            ?? throw new InvalidOperationException($"Cannot create {responseType.Name}; a parameterless constructor is required.");
        foreach (var property in responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = property.GetCustomAttribute<ApiFieldAttribute>();
            if (attribute is null || !property.CanWrite)
                continue;
            if (attribute.Flatten && IsNested(property.PropertyType))
            {
                property.SetValue(instance, MapCore(property.PropertyType, fields, namingPolicy, depth + 1));
                continue;
            }

            var key = string.IsNullOrEmpty(attribute.Name) ? namingPolicy.ConvertName(property.Name) : attribute.Name;
            if (!fields.TryGetValue(key, out var value))
                continue;
            if (value is null)
            {
                if (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    property.SetValue(instance, null);
                continue;
            }
            property.SetValue(instance, ConvertValue(value, property.PropertyType));
        }
        return instance;
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(string))
            return value;
        if (underlying.IsEnum)
            return Enum.Parse(underlying, value, ignoreCase: true);
        if (underlying == typeof(Guid))
            return Guid.Parse(value);
        if (underlying == typeof(DateOnly))
            return DateOnly.Parse(value, CultureInfo.InvariantCulture);
        if (underlying == typeof(DateTime))
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (underlying == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (underlying == typeof(bool))
            return bool.Parse(value);
        if (underlying.IsPrimitive || underlying == typeof(decimal))
            return System.Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        return JsonSerializer.Deserialize(value, targetType);
    }

    private static bool IsNested(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return !underlying.IsPrimitive && !underlying.IsEnum && underlying != typeof(string)
            && underlying != typeof(decimal) && underlying != typeof(DateTime)
            && underlying != typeof(DateTimeOffset) && underlying != typeof(DateOnly)
            && underlying != typeof(Guid) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying);
    }
}