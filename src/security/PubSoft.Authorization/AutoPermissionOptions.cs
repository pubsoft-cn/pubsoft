using System.Text;

namespace PubSoft.Authorization;


public sealed class AutoPermissionOptions
{
    public bool EnforceDerived { get; set; }
    public Dictionary<string, string> RouteModuleMap { get; } = new(StringComparer.OrdinalIgnoreCase);
}
