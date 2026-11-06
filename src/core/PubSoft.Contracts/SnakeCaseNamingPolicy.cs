using System.Text.RegularExpressions;

namespace PubSoft.Contracts;


public sealed class SnakeCaseNamingPolicy : INamingPolicy
{
    public static readonly SnakeCaseNamingPolicy Instance = new();

    public string ConvertName(string propertyName)
        => string.IsNullOrEmpty(propertyName)
            ? propertyName
            : Regex.Replace(propertyName, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}
