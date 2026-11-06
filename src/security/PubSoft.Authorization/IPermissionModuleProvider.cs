using System.Text;

namespace PubSoft.Authorization;

public interface IPermissionModuleProvider
{
    IReadOnlyCollection<string> All { get; }
    string GetDisplayName(string moduleKey);
    string? GetDescription(string moduleKey) => null;
    int? GetSortOrder(string moduleKey) => null;
    byte? GetAvailableFlags(string moduleKey) => null;
}
