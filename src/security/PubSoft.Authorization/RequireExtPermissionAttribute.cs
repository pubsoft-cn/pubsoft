using System.Text;

namespace PubSoft.Authorization;


/// <summary>Requires an extended (non-flag) permission bit on a module.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireExtPermissionAttribute(string moduleKey, uint extFlag, string clientCode = "") : Attribute
{
    /// <summary>The required module.</summary>
    public string ModuleKey { get; } = string.IsNullOrWhiteSpace(moduleKey)
        ? throw new ArgumentException("A permission module key is required.", nameof(moduleKey))
        : moduleKey;

    /// <summary>The required extension flag bit.</summary>
    public uint ExtFlag { get; } = extFlag != 0
        ? extFlag
        : throw new ArgumentOutOfRangeException(nameof(extFlag), "An extension permission flag must be non-zero.");

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
