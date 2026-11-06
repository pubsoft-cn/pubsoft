using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

[RequiresUnreferencedCode("Contract deserialization uses reflection over response members; preserve contract types under trimming.")]
[RequiresDynamicCode("Contract deserialization materializes nested and collection types at runtime.")]
public static class ContractJsonDeserializer
{
    public static object Deserialize(JsonDocument document, Type targetType, INamingPolicy namingPolicy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(namingPolicy);
        return DeserializeElement(document.RootElement, targetType, namingPolicy, 0);
    }

    internal static object DeserializeElement(
        JsonElement root,
        Type targetType,
        INamingPolicy namingPolicy,
        int depth = 0)
    {
        if (depth > 64)
            throw new JsonException("Contract object nesting exceeds the maximum supported depth.");
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected a JSON object for {targetType.Name}.");

        var instance = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Cannot create {targetType.Name}; a parameterless constructor is required.");
        foreach (var property in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = property.GetCustomAttribute<ApiFieldAttribute>();
            if (attribute is null || !property.CanWrite)
                continue;

            var propertyType = property.PropertyType;
            var isNested = IsNestedType(propertyType);
            if (attribute.Flatten && isNested)
            {
                property.SetValue(instance, DeserializeElement(root, propertyType, namingPolicy, depth + 1));
                continue;
            }

            var apiName = string.IsNullOrEmpty(attribute.Name)
                ? namingPolicy.ConvertName(property.Name)
                : attribute.Name;
            if (!TryGetProperty(root, apiName, out var value)
                && !TryGetProperty(root, CamelCaseNamingPolicy.Instance.ConvertName(property.Name), out value))
                continue;

            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
                    property.SetValue(instance, null);
                continue;
            }

            var converted = ConvertValue(value, propertyType, namingPolicy, depth + 1);
            property.SetValue(instance, converted);
        }
        return instance;
    }

    private static object? ConvertValue(JsonElement element, Type targetType, INamingPolicy namingPolicy, int depth)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (element.ValueKind == JsonValueKind.Object && IsNestedType(underlying))
            return DeserializeElement(element, underlying, namingPolicy, depth);
        if (element.ValueKind == JsonValueKind.Array && TryGetCollectionElementType(targetType, out var elementType, out var arrayType))
        {
            var values = element.EnumerateArray()
                .Select(item => ConvertValue(item, elementType, namingPolicy, depth + 1))
                .ToArray();
            if (arrayType)
            {
                var array = Array.CreateInstance(elementType, values.Length);
                for (var index = 0; index < values.Length; index++)
                    array.SetValue(values[index], index);
                return array;
            }

            var listType = typeof(List<>).MakeGenericType(elementType);
            if (targetType.IsAssignableFrom(listType))
                return CreateList(elementType, values);
        }
        return JsonValueConverter.Convert(element, targetType);
    }

    private static IList CreateList(Type elementType, object?[] values)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var value in values)
            list.Add(value);
        return list;
    }

    private static bool TryGetCollectionElementType(Type targetType, out Type elementType, out bool isArray)
    {
        isArray = targetType.IsArray;
        if (isArray)
        {
            elementType = targetType.GetElementType()!;
            return true;
        }

        var enumerable = targetType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(targetType)
            ? targetType
            : targetType.GetInterfaces().FirstOrDefault(type => type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        elementType = enumerable?.GetGenericArguments()[0]!;
        return enumerable is not null && targetType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType));
    }

    private static bool IsNestedType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return !underlying.IsPrimitive && !underlying.IsEnum && underlying != typeof(string)
            && underlying != typeof(decimal) && underlying != typeof(DateTime)
            && underlying != typeof(DateTimeOffset) && underlying != typeof(DateOnly)
            && underlying != typeof(Guid) && !typeof(IEnumerable).IsAssignableFrom(underlying);
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value);
    }
}