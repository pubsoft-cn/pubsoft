using System.Text;

namespace PubSoft.Authorization;

/// <summary>Supplies permission module metadata for discovery.</summary>
public interface IPermissionModuleProvider
{
    /// <summary>All module keys the provider knows.</summary>
    IReadOnlyCollection<string> All { get; }
    /// <summary>Display name for a module.</summary>
    string GetDisplayName(string moduleKey);
    /// <summary>Optional description.</summary>
    string? GetDescription(string moduleKey) => null;
    /// <summary>Optional sort order.</summary>
    int? GetSortOrder(string moduleKey) => null;
    /// <summary>Optional available-flags mask.</summary>
    byte? GetAvailableFlags(string moduleKey) => null;
}
