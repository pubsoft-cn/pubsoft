using System.Text.RegularExpressions;

namespace PubSoft.Contracts;


/// <summary>PascalCase (identity) naming policy.</summary>
public sealed class PascalCaseNamingPolicy : INamingPolicy
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly PascalCaseNamingPolicy Instance = new();

    /// <inheritdoc />
    public string ConvertName(string propertyName) => propertyName;
}
