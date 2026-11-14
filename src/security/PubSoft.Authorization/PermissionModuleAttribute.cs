namespace PubSoft.Authorization;


/// <summary>Declares the permission module a controller/action belongs to.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionModuleAttribute(string moduleKey, string clientCode = "") : Attribute
{
    /// <summary>The module key.</summary>
    public string ModuleKey { get; } = string.IsNullOrWhiteSpace(moduleKey)
        ? throw new ArgumentException("A permission module key is required.", nameof(moduleKey))
        : moduleKey;

    /// <summary>Client code the declaration applies to.</summary>
    public string ClientCode { get; } = ValidateClientCode(clientCode);

    /// <summary>Optional display name override.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Optional description.</summary>
    public string? Description { get; set; }
    /// <summary>Display ordering.</summary>
    public int SortOrder { get; set; }

    private static string ValidateClientCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= 64 && string.Equals(value, value.Trim(), System.StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(value));
    }
}
