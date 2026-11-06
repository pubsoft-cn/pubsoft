using System.Text.RegularExpressions;

namespace PubSoft.Contracts;


public sealed class PascalCaseNamingPolicy : INamingPolicy
{
    public static readonly PascalCaseNamingPolicy Instance = new();

    public string ConvertName(string propertyName) => propertyName;
}
