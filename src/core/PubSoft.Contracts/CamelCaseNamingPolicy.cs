namespace PubSoft.Contracts;

/// <summary>camelCase naming policy.</summary>
public sealed class CamelCaseNamingPolicy : INamingPolicy
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly CamelCaseNamingPolicy Instance = new();

    /// <inheritdoc />
    public string ConvertName(string propertyName)
        => string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0])
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
