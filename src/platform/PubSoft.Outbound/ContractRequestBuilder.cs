using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Builds request parameters from contract types (reflection-cached).</summary>
[RequiresUnreferencedCode("Contract request building uses reflection over request members; preserve contract types under trimming.")]
[PubSoft.Core.BoundedCost("builder output contract - callers populate the returned params dictionary before dispatch")]
public static class ContractRequestBuilder
{
    private static readonly ConcurrentDictionary<(Type Type, INamingPolicy Policy), FieldEntry[]> Cache = new();

    /// <summary>Builds string params with default naming (headers excluded).</summary>
    public static Dictionary<string, string?> BuildParams(object request)
        => BuildParams(request, CamelCaseNamingPolicy.Instance, FieldSource.Header);

    /// <summary>Builds string params with a naming policy.</summary>
    public static Dictionary<string, string?> BuildParams(object request, INamingPolicy namingPolicy)
        => BuildParams(request, namingPolicy, FieldSource.Header);

    /// <summary>Builds string params excluding field sources.</summary>
    public static Dictionary<string, string?> BuildParams(object request, params FieldSource[] excludeSources)
        => BuildParams(request, CamelCaseNamingPolicy.Instance, excludeSources);

    /// <summary>Builds string params with naming policy + exclusions.</summary>
    public static Dictionary<string, string?> BuildParams(
        object request,
        INamingPolicy namingPolicy,
        params FieldSource[] excludeSources)
    {
        var values = BuildObject(request, namingPolicy, excludeSources);
        return values.ToDictionary(pair => pair.Key, pair => FormatValue(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>Builds typed params with default naming.</summary>
    public static Dictionary<string, object?> BuildParamsObject(object request)
        => BuildParamsObject(request, CamelCaseNamingPolicy.Instance, FieldSource.Header);

    /// <summary>Builds typed params with a naming policy.</summary>
    public static Dictionary<string, object?> BuildParamsObject(object request, INamingPolicy namingPolicy)
        => BuildParamsObject(request, namingPolicy, FieldSource.Header);

    /// <summary>Builds typed params excluding field sources.</summary>
    public static Dictionary<string, object?> BuildParamsObject(object request, params FieldSource[] excludeSources)
        => BuildParamsObject(request, CamelCaseNamingPolicy.Instance, excludeSources);

    /// <summary>Builds typed params with naming policy + exclusions.</summary>
    public static Dictionary<string, object?> BuildParamsObject(
        object request,
        INamingPolicy namingPolicy,
        params FieldSource[] excludeSources)
        => BuildObject(request, namingPolicy, excludeSources);

    private static Dictionary<string, object?> BuildObject(
        object request,
        INamingPolicy namingPolicy,
        FieldSource[] excludeSources)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(namingPolicy);
        var excluded = new HashSet<FieldSource>(excludeSources ?? []);
        return BuildObject(request, namingPolicy, excluded, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static Dictionary<string, object?> BuildObject(
        object request,
        INamingPolicy namingPolicy,
        HashSet<FieldSource> excluded,
        HashSet<object> path)
    {
        var type = request.GetType();
        var tracksReference = !type.IsValueType;
        if (tracksReference && !path.Add(request))
            throw new InvalidOperationException($"Flattened contract contains a reference cycle at {type.Name}.");

        try
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in Cache.GetOrAdd((type, namingPolicy), BuildEntries))
            {
                if (excluded.Contains(entry.Source))
                    continue;

                var value = entry.Getter(request);
                if (ShouldOmit(entry, value))
                    continue;

                if (entry.Flatten && entry.IsNested && value is not null)
                {
                    foreach (var nested in BuildObject(value, namingPolicy, excluded, path))
                        AddUnique(result, nested.Key, nested.Value);
                }
                else
                {
                    AddUnique(result, entry.ApiName, value);
                }
            }
            return result;
        }
        finally
        {
            if (tracksReference)
                path.Remove(request);
        }
    }

    private static bool ShouldOmit(FieldEntry entry, object? value)
        => entry.Behavior switch
        {
            EmitBehavior.IgnoreIfNull => value is null,
            EmitBehavior.IgnoreIfDefault => value is null || Equals(
                value,
                entry.PropertyType.IsValueType ? Activator.CreateInstance(entry.PropertyType) : null),
            _ => false
        };

    private static void AddUnique(Dictionary<string, object?> values, string key, object? value)
    {
        if (!values.TryAdd(key, value))
            throw new InvalidOperationException($"Multiple contract fields map to wire name '{key}'.");
    }

    private static string? FormatValue(object? value)
        => value switch
        {
            null => null,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static FieldEntry[] BuildEntries((Type Type, INamingPolicy Policy) key)
        => key.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ApiFieldAttribute>()))
            .Where(item => item.Attribute is not null && item.Property.CanRead)
            .OrderBy(item => item.Attribute!.Order)
            .ThenBy(item => item.Property.MetadataToken)
            .Select(item =>
            {
                var property = item.Property;
                var attribute = item.Attribute!;
                var type = property.PropertyType;
                var nested = !IsSimpleType(type)
                    && !typeof(System.Collections.IDictionary).IsAssignableFrom(type)
                    && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
                return new FieldEntry(
                    string.IsNullOrEmpty(attribute.Name) ? key.Policy.ConvertName(property.Name) : attribute.Name,
                    attribute.Source,
                    attribute.Emit,
                    attribute.Flatten,
                    type,
                    nested,
                    property.GetValue);
            })
            .ToArray();

    private static bool IsSimpleType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string)
            || underlying == typeof(decimal) || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)
            || underlying == typeof(Guid);
    }

    private sealed record FieldEntry(
        string ApiName,
        FieldSource Source,
        EmitBehavior Behavior,
        bool Flatten,
        Type PropertyType,
        bool IsNested,
        Func<object, object?> Getter);
}