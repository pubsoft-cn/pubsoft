using System.Text;

namespace PubSoft.Authorization;


public sealed class PermissionModuleRegistry
{
    private readonly List<IPermissionModuleProvider> _providers = [];

    public PermissionModuleRegistry()
    {
    }

    public PermissionModuleRegistry(IEnumerable<IPermissionModuleProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers)
            Register(provider);
    }

    public string[] AllModules => _providers
        .SelectMany(provider => provider.All)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public void Register(IPermissionModuleProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
    }

    public string GetDisplayName(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        foreach (var provider in _providers)
        {
            var displayName = provider.GetDisplayName(moduleKey);
            if (!string.IsNullOrWhiteSpace(displayName) && !string.Equals(displayName, moduleKey, System.StringComparison.Ordinal))
                return displayName;
        }

        return moduleKey;
    }

    public string? GetDescription(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        foreach (var provider in _providers)
        {
            var description = provider.GetDescription(moduleKey);
            if (!string.IsNullOrWhiteSpace(description))
                return description;
        }

        return null;
    }
}
