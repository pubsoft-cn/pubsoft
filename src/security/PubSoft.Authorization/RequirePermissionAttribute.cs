using System.Text;

namespace PubSoft.Authorization;


/// <summary>Requires flag permissions on a module; repeatable for alternative grants.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute(string moduleKey, PermissionFlags flags, string clientCode = "") : Attribute
{
    /// <summary>The required module.</summary>
    public string ModuleKey { get; } = string.IsNullOrWhiteSpace(moduleKey)
        ? throw new ArgumentException("A permission module key is required.", nameof(moduleKey))
        : moduleKey;

    /// <summary>The required flag bits.</summary>
    public PermissionFlags Flags { get; } = flags;
    /// <summary>Client code the requirement applies to.</summary>
    public string ClientCode { get; } = ValidateClientCode(clientCode);

    private static string ValidateClientCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= 64 && string.Equals(value, value.Trim(), System.StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(value));
    }
}
