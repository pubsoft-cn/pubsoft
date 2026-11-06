using System.Text;

namespace PubSoft.Authorization;


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionModuleAttribute(string moduleKey, string clientCode = "") : Attribute
{
    public string ModuleKey { get; } = string.IsNullOrWhiteSpace(moduleKey)
        ? throw new ArgumentException("A permission module key is required.", nameof(moduleKey))
        : moduleKey;

    public string ClientCode { get; } = ValidateClientCode(clientCode);

    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    private static string ValidateClientCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= 64 && string.Equals(value, value.Trim(), System.StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(value));
    }
}
