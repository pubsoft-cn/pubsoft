using System.Text;

namespace PubSoft.Authorization;


/// <summary>Aggregates module providers into a single lookup surface.</summary>
public sealed class PermissionModuleRegistry
{
    private readonly List<IPermissionModuleProvider> _providers = [];

    /// <summary>Creates an empty registry.</summary>
    public PermissionModuleRegistry()
    {
    }

    /// <summary>Creates a registry pre-loaded with providers.</summary>
    public PermissionModuleRegistry(IEnumerable<IPermissionModuleProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers)
            Register(provider);
    }

    /// <summary>All module keys across providers.</summary>
    public string[] AllModules => _providers
        .SelectMany(provider => provider.All)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Adds a provider.</summary>
    public void Register(IPermissionModuleProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
    }

    /// <summary>Display name for a module; falls back to the key.</summary>
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

    /// <summary>Description for a module, when provided.</summary>
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
