using PubSoft.Core;
namespace PubSoft.Authorization;

[Flags]
public enum PermissionFlags : byte
{
    None = 0,
    Query = 1,
    Create = 2,
    Modify = 4,
    Delete = 8,
    Audit = 16,
    Custom = 32,
    All = Query | Create | Modify | Delete | Audit | Custom
}
