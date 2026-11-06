using System.Text.Json;

namespace PubSoft.Domain;

/// <summary>A permission module exposed for granting - declares which flag bits and named permissions exist.</summary>
[PubSoft.Core.BoundedCost("data object - timestamp assigned by infrastructure interceptors, not injected TimeProvider")]
public sealed class PermissionModule : Entity<long>, IMultiTenant
{
    private const byte DefinedPermissionFlags = 0x3F;
    private const int MaxCustomPermissionsJsonLength = 4000;

    /// <summary>Creates a module; keys/names must be non-empty and trimmed.</summary>
    public PermissionModule(
        long id,
        string clientCode,
        string moduleKey,
        string displayName,
        long? tenantId = null,
        byte availableFlags = 63,
        string? description = null,
        string? customPermissionsJson = null,
        int sortOrder = 0)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        ValidateClientCode(clientCode);
        ValidateText(moduleKey, 128, nameof(moduleKey));
        ValidateText(displayName, 200, nameof(displayName));
        ValidateOptionalText(description, 1000, nameof(description));
        ValidateAvailableFlags(availableFlags);
        ValidateCustomPermissionsJson(customPermissionsJson);

        Id = id;
        TenantId = tenantId;
        ClientCode = clientCode;
        ModuleKey = moduleKey;
        DisplayName = displayName;
        Description = description;
        AvailableFlags = availableFlags;
        CustomPermissionsJson = customPermissionsJson;
        SortOrder = sortOrder;
        CreationTime = DateTime.UtcNow;
    }

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PermissionModule()
    {
    }

    /// <summary>Owning tenant; null for host-level rows.</summary>
    public long? TenantId { get; set; }
    /// <summary>Client/application code the module belongs to.</summary>
    public string ClientCode { get; private set; } = string.Empty;
    /// <summary>The stable module key used by grant rows and attributes.</summary>
    public string ModuleKey { get; private set; } = string.Empty;
    /// <summary>Human-readable module name for admin UI.</summary>
    public string DisplayName { get; private set; } = string.Empty;
    /// <summary>Optional module description.</summary>
    public string? Description { get; private set; }
    /// <summary>Bitmask of the standard operations this module supports.</summary>
    public byte AvailableFlags { get; private set; }
    /// <summary>JSON list of named permissions outside the bitmask space.</summary>
    public string? CustomPermissionsJson { get; private set; }
    /// <summary>Display ordering in the permission-assignment UI.</summary>
    public int SortOrder { get; private set; }
    /// <summary>Whether the module is visible/grantable.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>When the module was registered (UTC).</summary>
    public DateTime CreationTime { get; private set; }
    /// <summary>When the module last changed (UTC).</summary>
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Updates display metadata; null arguments keep current values.</summary>
    public void Update(
        string displayName,
        string? description,
        byte? availableFlags,
        string? customPermissionsJson,
        int? sortOrder)
    {
        ValidateText(displayName, 200, nameof(displayName));
        ValidateOptionalText(description, 1000, nameof(description));
        if (availableFlags.HasValue)
            ValidateAvailableFlags(availableFlags.Value);
        ValidateCustomPermissionsJson(customPermissionsJson);

        DisplayName = displayName;
        Description = description;
        if (availableFlags.HasValue)
            AvailableFlags = availableFlags.Value;
        if (customPermissionsJson is not null)
            CustomPermissionsJson = customPermissionsJson;
        if (sortOrder.HasValue)
            SortOrder = sortOrder.Value;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateClientCode(string clientCode)
    {
        ArgumentNullException.ThrowIfNull(clientCode);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", nameof(clientCode));
    }

    private static void ValidateText(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), System.StringComparison.Ordinal) || value.Length > maxLength)
            throw new ArgumentException($"Value must be trimmed and no longer than {maxLength} characters.", parameterName);
    }

    private static void ValidateOptionalText(string? value, int maxLength, string parameterName)
    {
        if (value is not null && (!string.Equals(value, value.Trim(), System.StringComparison.Ordinal) || value.Length > maxLength))
            throw new ArgumentException($"Value must be trimmed and no longer than {maxLength} characters.", parameterName);
    }

    private static void ValidateAvailableFlags(byte availableFlags)
    {
        if ((availableFlags & ~DefinedPermissionFlags) != 0)
            throw new ArgumentOutOfRangeException(nameof(availableFlags), "Available flags may contain only defined permission bits.");
    }

    private static void ValidateCustomPermissionsJson(string? json)
    {
        if (json is null)
            return;
        if (json.Length > MaxCustomPermissionsJsonLength)
            throw new ArgumentException(
                $"Custom permission metadata cannot exceed {MaxCustomPermissionsJsonLength} characters.",
                nameof(json));

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Custom permissions must be a JSON array of strings.", nameof(json));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Custom permissions must contain only strings.", nameof(json));
            var name = item.GetString();
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), System.StringComparison.Ordinal) || !names.Add(name))
                throw new ArgumentException("Custom permission names must be non-empty, trimmed, and unique.", nameof(json));
        }
    }
}