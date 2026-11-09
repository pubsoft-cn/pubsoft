using System.Text.RegularExpressions;

namespace PubSoft.Contracts;


/// <summary>snake_case naming policy.</summary>
public sealed class SnakeCaseNamingPolicy : INamingPolicy
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SnakeCaseNamingPolicy Instance = new();

    private static readonly System.Text.RegularExpressions.Regex UpperBoundary = new("(?<!^)([A-Z])", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <inheritdoc />
    public string ConvertName(string propertyName)
        => string.IsNullOrEmpty(propertyName)
            ? propertyName
            : UpperBoundary.Replace(propertyName, "_$1").ToLowerInvariant();
}
