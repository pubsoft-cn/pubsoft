namespace PubSoft.Authorization;


public sealed class PermissionGrant
{
    public PermissionGrant(
        long? tenantId,
        string moduleKey,
        PermissionFlags flags,
        uint extensionFlags = 0,
        IEnumerable<string>? customPermissions = null,
        string clientCode = "")
    {
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId), "Tenant ID must be positive when specified.");
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        if (!string.Equals(moduleKey, moduleKey.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Permission module keys must have no surrounding whitespace.", nameof(moduleKey));
        ValidateClientCode(clientCode, nameof(clientCode));
        if ((flags & ~PermissionFlags.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags), "Permission grants may contain only defined flag bits.");
        var customPermissionSnapshot = (customPermissions ?? []).ToArray();
        if (customPermissionSnapshot.Any(permission => string.IsNullOrWhiteSpace(permission)
            || !string.Equals(permission, permission.Trim(), System.StringComparison.Ordinal)))
            throw new ArgumentException("Custom permission names must be non-empty and have no surrounding whitespace.", nameof(customPermissions));
        if (flags == PermissionFlags.None && extensionFlags == 0 && customPermissionSnapshot.Length == 0)
            throw new ArgumentException("A permission grant must contain at least one permission.");

        TenantId = tenantId;
        ClientCode = clientCode;
        ModuleKey = moduleKey;
        Flags = flags;
        ExtensionFlags = extensionFlags;
        CustomPermissions = Array.AsReadOnly(customPermissionSnapshot);
    }

    public long? TenantId { get; }
    public string ClientCode { get; }
    public string ModuleKey { get; }
    public PermissionFlags Flags { get; }
    public uint ExtensionFlags { get; }
    public IReadOnlyList<string> CustomPermissions { get; }

    private static void ValidateClientCode(string clientCode, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(clientCode, parameterName);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", parameterName);
    }
}
