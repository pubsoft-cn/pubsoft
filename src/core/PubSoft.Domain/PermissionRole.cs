namespace PubSoft.Domain;

public sealed class PermissionRole : Entity<long>, IMultiTenant
{
    private string _clientCode = string.Empty;

    public long? TenantId { get; set; }
    public string ClientCode
    {
        get => _clientCode;
        set
        {
            ValidateClientCode(value);
            _clientCode = value;
        }
    }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public PermissionRole()
    {
    }

    public PermissionRole(long id, string name, long? tenantId = null, string clientCode = "")
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        ValidateClientCode(clientCode);

        Id = id;
        Name = name.Trim();
        TenantId = tenantId;
        ClientCode = clientCode;
    }

    private static void ValidateClientCode(string clientCode)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
    }
}

public sealed class SubjectPermissionRole : Entity<long>, IMultiTenant
{
    private string _clientCode = string.Empty;

    public long? TenantId { get; set; }
    public string ClientCode
    {
        get => _clientCode;
        set
        {
            ValidateClientCode(value);
            _clientCode = value;
        }
    }
    public long SubjectId { get; set; }
    public long RoleId { get; set; }

    public SubjectPermissionRole()
    {
    }

    public SubjectPermissionRole(long id, long subjectId, long roleId, long? tenantId = null, string clientCode = "")
    {
        if (id <= 0 || subjectId <= 0 || roleId <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "IDs must be positive.");
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        ValidateClientCode(clientCode);

        Id = id;
        SubjectId = subjectId;
        RoleId = roleId;
        TenantId = tenantId;
        ClientCode = clientCode;
    }

    private static void ValidateClientCode(string clientCode)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
    }
}

public sealed class PermissionGrantRecord : Entity<long>, IMultiTenant
{
    public long? TenantId { get; set; }
    public long RoleId { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public byte Flags { get; set; }
    public uint ExtensionFlags { get; set; }
    public string? CustomPermissionName { get; set; }
    private PermissionGrantRecord()
    {
    }

    public PermissionGrantRecord(
        long id,
        long roleId,
        string moduleKey,
        long? tenantId = null,
        byte flags = 0,
        uint extensionFlags = 0,
        string? customPermissionName = null)
    {
        if (id <= 0 || roleId <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        if (!string.Equals(moduleKey, moduleKey.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Module keys must have no surrounding whitespace.", nameof(moduleKey));
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(roleId), "Specified IDs must be positive.");

        Id = id;
        RoleId = roleId;
        ModuleKey = moduleKey;
        TenantId = tenantId;
        SetPermissions(flags, extensionFlags, customPermissionName);
    }

    public void SetPermissions(byte flags, uint extensionFlags, string? customPermissionName)
    {
        if (flags == 0 && extensionFlags == 0 && string.IsNullOrWhiteSpace(customPermissionName))
            throw new ArgumentException("A grant must contain at least one permission.");
        if (customPermissionName is not null &&
            (string.IsNullOrWhiteSpace(customPermissionName) || !string.Equals(customPermissionName, customPermissionName.Trim(), System.StringComparison.Ordinal)))
            throw new ArgumentException("Custom permission names must be non-empty and trimmed.", nameof(customPermissionName));

        Flags = flags;
        ExtensionFlags = extensionFlags;
        CustomPermissionName = customPermissionName;
    }
}