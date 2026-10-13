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

public sealed class SnakeCaseNamingPolicy : INamingPolicy
{
    public static readonly SnakeCaseNamingPolicy Instance = new();

    public string ConvertName(string propertyName)
        => string.IsNullOrEmpty(propertyName)
            ? propertyName
            : Regex.Replace(propertyName, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}

public sealed class PascalCaseNamingPolicy : INamingPolicy
{
    public static readonly PascalCaseNamingPolicy Instance = new();

    public string ConvertName(string propertyName) => propertyName;
}