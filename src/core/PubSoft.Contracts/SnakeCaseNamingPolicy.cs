using System.Text.RegularExpressions;

namespace PubSoft.Contracts;


/// <summary>snake_case naming policy.</summary>
public sealed class SnakeCaseNamingPolicy : INamingPolicy
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SnakeCaseNamingPolicy Instance = new();

    /// <inheritdoc />
    public string ConvertName(string propertyName)
        => string.IsNullOrEmpty(propertyName)
            ? propertyName
            : Regex.Replace(propertyName, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}
