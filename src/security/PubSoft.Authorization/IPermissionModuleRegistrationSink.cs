using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;

/// <summary>Receives discovered modules for persistence (implemented by the host).</summary>
public interface IPermissionModuleRegistrationSink
{
    /// <summary>Persists the discovered module set.</summary>
    Task RegisterAsync(IReadOnlyList<DiscoveredPermissionModule> modules, CancellationToken cancellationToken = default);
}

/// <summary>Scans MVC action descriptors into discovered permission modules.</summary>
public sealed class PermissionModuleDiscovery
{
    private readonly IActionDescriptorCollectionProvider _actions;
    private readonly IEnumerable<IPermissionModuleProvider> _providers;
    private readonly PermissionModuleDiscoveryOptions _options;

    /// <summary>Creates the scanner over action descriptors and providers.</summary>
    public PermissionModuleDiscovery(
        IActionDescriptorCollectionProvider actions,
        IEnumerable<IPermissionModuleProvider> providers,
        IOptions<PermissionModuleDiscoveryOptions>? options = null)
    {
        _actions = actions;
        _providers = providers;
        _options = options?.Value ?? new PermissionModuleDiscoveryOptions();
    }

    /// <summary>Scans and returns the discovered module set.</summary>
    public IReadOnlyList<DiscoveredPermissionModule> Scan()
    {
        var modules = new Dictionary<string, ModuleAcc>(StringComparer.OrdinalIgnoreCase);
        var providerOrder = ProviderOrderMap();

        foreach (var descriptor in _actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (descriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) continue;
            var moduleKey = _options.ModuleKeyResolver?.Invoke(descriptor) ?? ResolveRouteModule(descriptor);
            if (moduleKey == null) continue;
            if (!modules.TryGetValue(moduleKey, out var acc))
            {
                acc = new ModuleAcc();
                modules[moduleKey] = acc;
            }
            acc.Merge(descriptor, _options);
        }

        foreach (var key in providerOrder.Keys)
            modules.TryAdd(key, new ModuleAcc());

        return modules.Select(kv =>
        {
            var (key, acc) = (kv.Key, kv.Value);
            var provider = _providers.FirstOrDefault(p => p.All.Contains(key, StringComparer.OrdinalIgnoreCase));
            return new DiscoveredPermissionModule
            {
                ModuleKey = key,
                DisplayName = acc.PinDisplayName ?? provider?.GetDisplayName(key) ?? key,
                Description = acc.PinDescription ?? provider?.GetDescription(key),
                AvailableFlags = acc.PinAvailableFlags ?? provider?.GetAvailableFlags(key) ?? 63,
                CustomPermissions = acc.CustomPermissions.OrderBy(x => x).ToList(),
                SortOrder = acc.PinSortOrder > 0 ? acc.PinSortOrder.Value
                    : provider?.GetSortOrder(key) ?? (providerOrder.TryGetValue(key, out var i) ? i * 10 : 9999),
                ApiPaths = acc.ApiPaths.OrderBy(x => x).ToList(),
            };
        }).OrderBy(m => m.SortOrder).ThenBy(m => m.ModuleKey).ToList();
    }

    private string? ResolveRouteModule(ControllerActionDescriptor action)
    {
        var route = action.AttributeRouteInfo?.Template ?? action.RouteValues["controller"] ?? "";
        foreach (var (prefix, key) in _options.RouteModuleMap)
            if (route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return key;
        return null;
    }

    private Dictionary<string, int> ProviderOrderMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var i = 1;
        foreach (var key in _providers.SelectMany(p => p.All).Distinct())
            map.TryAdd(key, i++);
        return map;
    }

    private sealed class ModuleAcc
    {
        public readonly List<string> CustomPermissions = new();
        public readonly List<string> ApiPaths = new();
        public string? PinDisplayName;
        public string? PinDescription;
        public int? PinSortOrder;
        public byte? PinAvailableFlags;

        public void Merge(ControllerActionDescriptor action, PermissionModuleDiscoveryOptions options)
        {
            PinDisplayName ??= options.PinDisplayNameResolver?.Invoke(action);
            PinDescription ??= options.PinDescriptionResolver?.Invoke(action);
            PinSortOrder ??= options.PinSortOrderResolver?.Invoke(action);
            PinAvailableFlags ??= options.PinAvailableFlagsResolver?.Invoke(action);
            if (options.CustomPermissionsExtractor is { } extractor)
                CustomPermissions.AddRange(extractor(action));
            var methods = action.EndpointMetadata.OfType<HttpMethodActionConstraint>()
                .SelectMany(m => m.HttpMethods).DefaultIfEmpty("GET");
            var route = action.AttributeRouteInfo?.Template ?? action.RouteValues["controller"] ?? "";
            foreach (var m in methods)
                ApiPaths.Add($"{m} /{route.TrimStart('/')}");
        }
    }
}
