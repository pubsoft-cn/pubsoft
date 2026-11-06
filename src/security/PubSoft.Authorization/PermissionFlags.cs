using PubSoft.Core;
namespace PubSoft.Authorization;

/// <summary>Standard permission bitmask (module-scoped; named permissions live outside the bit space).</summary>
[Flags]
public enum PermissionFlags : byte
{
    /// <summary>No permissions.</summary>
    None = 0,
    /// <summary>Read/list permission.</summary>
    Query = 1,
    /// <summary>Create permission.</summary>
    Create = 2,
    /// <summary>Update permission.</summary>
    Modify = 4,
    /// <summary>Delete permission.</summary>
    Delete = 8,
    /// <summary>Audit-log access.</summary>
    Audit = 16,
    /// <summary>Escape hatch - flags beyond this bit use CustomPermissions/ExtFlag.</summary>
    Custom = 32,
    /// <summary>Every defined bit.</summary>
    All = Query | Create | Modify | Delete | Audit | Custom
}
