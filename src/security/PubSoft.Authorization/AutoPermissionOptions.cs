using System.Text;

namespace PubSoft.Authorization;


/// <summary>Options for auto-derived permission enforcement.</summary>
public sealed class AutoPermissionOptions
{
    /// <summary>Whether derived requirements are enforced (opt-in).</summary>
    public bool EnforceDerived { get; set; }
    /// <summary>Route-prefix → module-key map.</summary>
    public Dictionary<string, string> RouteModuleMap { get; } = new(StringComparer.OrdinalIgnoreCase);
}
