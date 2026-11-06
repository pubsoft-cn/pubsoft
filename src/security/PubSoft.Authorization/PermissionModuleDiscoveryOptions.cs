using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;


public sealed class PermissionModuleDiscoveryOptions
{
    public Func<ControllerActionDescriptor, string?>? ModuleKeyResolver { get; set; }
    public Func<ControllerActionDescriptor, string?>? PinDisplayNameResolver { get; set; }
    public Func<ControllerActionDescriptor, string?>? PinDescriptionResolver { get; set; }
    public Func<ControllerActionDescriptor, int?>? PinSortOrderResolver { get; set; }
    public Func<ControllerActionDescriptor, byte?>? PinAvailableFlagsResolver { get; set; }
    public Func<ControllerActionDescriptor, IEnumerable<string>>? CustomPermissionsExtractor { get; set; }
    public Dictionary<string, string> RouteModuleMap { get; } = new(StringComparer.OrdinalIgnoreCase);
}
