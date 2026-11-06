using PubSoft.Core;
namespace PubSoft.Authorization;


/// <summary>Supplies permission definitions for registration.</summary>
public interface IPermissionDefinitionProvider
{
    /// <summary>Returns the definitions the provider declares.</summary>
    IReadOnlyList<PermissionDefinition> GetDefinitions();
}
