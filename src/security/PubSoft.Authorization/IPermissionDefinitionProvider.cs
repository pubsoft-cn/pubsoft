using PubSoft.Core;
namespace PubSoft.Authorization;


public interface IPermissionDefinitionProvider
{
    IReadOnlyList<PermissionDefinition> GetDefinitions();
}
