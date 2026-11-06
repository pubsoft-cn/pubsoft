using System.Text;

namespace PubSoft.Authorization;


[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireCustomPermissionAttribute(string moduleKey, string permissionName, string clientCode = "") : Attribute
{
    public string ModuleKey { get; } = string.IsNullOrWhiteSpace(moduleKey)
        ? throw new ArgumentException("A permission module key is required.", nameof(moduleKey))
        : moduleKey;

    public string PermissionName { get; } = string.IsNullOrWhiteSpace(permissionName)
        ? throw new ArgumentException("A custom permission name is required.", nameof(permissionName))
        : permissionName;

    public string ClientCode { get; } = ValidateClientCode(clientCode);

    private static string ValidateClientCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= 64 && string.Equals(value, value.Trim(), System.StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(value));
    }
}
