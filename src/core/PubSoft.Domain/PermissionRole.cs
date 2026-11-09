namespace PubSoft.Domain;

/// <summary>A named role that carries permission grants; scoped per client code and tenant.</summary>
public sealed class PermissionRole : Entity<long>, IMultiTenant
{
    private string _clientCode = string.Empty;

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    public PermissionRole()
    {
    }

    /// <summary>Creates a role; name required and trimmed.</summary>
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

    /// <summary>Owning tenant; null for host-level rows.</summary>
    public long? TenantId { get; set; }
    /// <summary>Client/application code the role applies to (empty = all clients).</summary>
    public string ClientCode
    {
        get => _clientCode;
        set
        {
            ValidateClientCode(value);
            _clientCode = value;
        }
    }
    /// <summary>Role name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Whether the role grants permissions.</summary>
    public bool IsEnabled { get; set; } = true;

    private static void ValidateClientCode(string clientCode)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
    }
}

/// <summary>Binds a subject (user or client) to a role.</summary>
public sealed class SubjectPermissionRole : Entity<long>, IMultiTenant
{
    private string _clientCode = string.Empty;

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    public SubjectPermissionRole()
    {
    }

    /// <summary>Binds a subject to a role.</summary>
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

    /// <summary>Owning tenant; null for host-level roles.</summary>
    public long? TenantId { get; set; }
    /// <summary>Client code the role applies to.</summary>
    public string ClientCode
    {
        get => _clientCode;
        set
        {
            ValidateClientCode(value);
            _clientCode = value;
        }
    }
    /// <summary>The bound subject (user or client id).</summary>
    public long SubjectId { get; set; }
    /// <summary>The bound role - The role receiving the grant.</summary>
    public long RoleId { get; set; }

    private static void ValidateClientCode(string clientCode)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
    }
}

/// <summary>One permission grant row: role + module + bit flags + optional named permission.</summary>
public sealed class PermissionGrantRecord : Entity<long>, IMultiTenant
{
    /// <summary>Creates a grant; <paramref name="moduleKey"/> must be trimmed and non-empty.</summary>
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
            throw new ArgumentOutOfRangeException(nameof(tenantId), "Specified IDs must be positive.");

        Id = id;
        RoleId = roleId;
        ModuleKey = moduleKey;
        TenantId = tenantId;
        SetPermissions(flags, extensionFlags, customPermissionName);
    }

    private PermissionGrantRecord()
    {
    }

    /// <summary>Owning tenant.</summary>
    public long? TenantId { get; set; }
    /// <summary>The granted role.</summary>
    public long RoleId { get; set; }
    /// <summary>The module the grant applies to.</summary>
    public string ModuleKey { get; set; } = string.Empty;
    /// <summary>Standard bitmask grants (Query/Create/Modify/Delete/Audit/Custom).</summary>
    public byte Flags { get; set; }
    /// <summary>Extended bitmask grants for host-defined operations beyond the standard bits.</summary>
    public uint ExtensionFlags { get; set; }

    /// <summary>A named permission outside the bitmask space (e.g. Export, Approve).</summary>
    public string? CustomPermissionName { get; set; }

    /// <summary>Replaces the grant contents; at least one of flags/extension/name must be set.</summary>
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