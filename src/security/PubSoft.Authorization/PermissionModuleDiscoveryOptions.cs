using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;


/// <summary>How permission modules are discovered from MVC descriptors.</summary>
public sealed class PermissionModuleDiscoveryOptions
{
    /// <summary>Custom module-key resolver; defaults to convention/attribute.</summary>
    public Func<ControllerActionDescriptor, string?>? ModuleKeyResolver { get; set; }
    /// <summary>Display-name resolver.</summary>
    public Func<ControllerActionDescriptor, string?>? PinDisplayNameResolver { get; set; }
    /// <summary>Description resolver.</summary>
    public Func<ControllerActionDescriptor, string?>? PinDescriptionResolver { get; set; }
    /// <summary>Sort-order resolver.</summary>
    public Func<ControllerActionDescriptor, int?>? PinSortOrderResolver { get; set; }
    /// <summary>Available-flags resolver.</summary>
    public Func<ControllerActionDescriptor, byte?>? PinAvailableFlagsResolver { get; set; }
    /// <summary>Extracts named permissions an action requires.</summary>
    public Func<ControllerActionDescriptor, IEnumerable<string>>? CustomPermissionsExtractor { get; set; }
    /// <summary>Route-prefix -> module-key map for endpoints without explicit keys.</summary>
    public Dictionary<string, string> RouteModuleMap { get; } = new(StringComparer.OrdinalIgnoreCase);
}
