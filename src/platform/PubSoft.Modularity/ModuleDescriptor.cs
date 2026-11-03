using System.Reflection;

namespace PubSoft.Modularity;

public sealed record ModuleDescriptor(
    string Name,
    Type ModuleType,
    Type[]? Dependencies = null,
    bool Optional = false)
{
    public static ModuleDescriptor FromType(Type moduleType, string? name = null, bool optional = false)
        => new(name ?? moduleType.Name,
               moduleType,
               moduleType.GetCustomAttribute<DependsOnAttribute>()?.ModuleTypes,
               optional);
}
