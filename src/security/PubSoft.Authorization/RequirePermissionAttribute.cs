using System.Text;

namespace PubSoft.Authorization;


[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute(string moduleKey, PermissionFlags flags, string clientCode = "") : Attribute
{
    public string ModuleKey { get; } = string.IsNullOrWhiteSpace(moduleKey)
        ? throw new ArgumentException("A permission module key is required.", nameof(moduleKey))
        : moduleKey;

    public PermissionFlags Flags { get; } = flags;
    public string ClientCode { get; } = ValidateClientCode(clientCode);

    private static string ValidateClientCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= 64 && string.Equals(value, value.Trim(), System.StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(value));
    }
}
