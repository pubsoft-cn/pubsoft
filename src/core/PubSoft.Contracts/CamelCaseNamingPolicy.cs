using System.Text.RegularExpressions;

namespace PubSoft.Contracts;

public sealed class CamelCaseNamingPolicy : INamingPolicy
{
    public static readonly CamelCaseNamingPolicy Instance = new();

    public string ConvertName(string propertyName)
        => string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0])
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
