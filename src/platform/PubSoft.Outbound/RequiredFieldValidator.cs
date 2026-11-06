using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Validates required contract fields.</summary>
[RequiresUnreferencedCode("Required-field validation uses reflection over request members; preserve contract types under trimming.")]
public static class RequiredFieldValidator
{
    private static readonly ConcurrentDictionary<Type, RequiredEntry[]> Cache = new();

    /// <summary>Returns the first missing-field error, or null when valid.</summary>
    public static string? Validate(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var entry in Cache.GetOrAdd(request.GetType(), BuildEntries))
        {
            var value = entry.Getter(request);
            var valueType = Nullable.GetUnderlyingType(entry.PropertyType) ?? entry.PropertyType;
            if (value is null || (value is string text && string.IsNullOrWhiteSpace(text))
                || (valueType.IsValueType && Equals(value, Activator.CreateInstance(valueType))))
                return $"{entry.DisplayName} required.";
        }
        return null;
    }

    private static RequiredEntry[] BuildEntries(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ApiFieldAttribute>()))
            .Where(item => item.Attribute?.IsRequired == true && item.Property.CanRead)
            .OrderBy(item => item.Attribute!.Order)
            .ThenBy(item => item.Property.MetadataToken)
            .Select(item => new RequiredEntry(
                string.IsNullOrEmpty(item.Attribute!.Name) ? item.Property.Name : item.Attribute.Name,
                item.Property.PropertyType,
                item.Property.GetValue))
            .ToArray();

    private sealed record RequiredEntry(string DisplayName, Type PropertyType, Func<object, object?> Getter);
}