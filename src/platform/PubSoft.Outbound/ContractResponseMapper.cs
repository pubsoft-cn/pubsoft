using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Maps JSON responses to contract types.</summary>
[RequiresUnreferencedCode("Contract response mapping uses reflection over response members; preserve contract types under trimming.")]
[RequiresDynamicCode("Contract response mapping materializes nested and collection types at runtime.")]
public static class ContractResponseMapper
{
    /// <summary>Maps a JSON element to <typeparamref name="T"/>.</summary>
    public static T Map<T>(JsonElement jsonElement) where T : new()
        => (T)Map(typeof(T), jsonElement, CamelCaseNamingPolicy.Instance);

    /// <summary>Maps a JSON element to the given type.</summary>
    public static object Map(Type responseType, JsonElement jsonElement)
        => Map(responseType, jsonElement, CamelCaseNamingPolicy.Instance);

    /// <summary>Maps with an explicit naming policy.</summary>
    public static object Map(Type responseType, JsonElement jsonElement, INamingPolicy namingPolicy)
    {
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(namingPolicy);
        return ContractJsonDeserializer.DeserializeElement(jsonElement, responseType, namingPolicy);
    }

    /// <summary>Maps to <typeparamref name="T"/> with an explicit naming policy.</summary>
    public static T Map<T>(JsonElement jsonElement, INamingPolicy namingPolicy) where T : new()
        => (T)Map(typeof(T), jsonElement, namingPolicy);
}