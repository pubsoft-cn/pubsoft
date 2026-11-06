using System.Reflection;

namespace PubSoft.Modularity;

/// <summary>A module's identity + dependency wiring.</summary>
/// <param name="Name">Module name.</param>
/// <param name="ModuleType">Module CLR type.</param>
/// <param name="Dependencies">Required modules.</param>
/// <param name="Optional">Whether failure degrades instead of failing startup.</param>
public sealed record ModuleDescriptor(
    string Name,
    Type ModuleType,
    Type[]? Dependencies = null,
    bool Optional = false)
{
    /// <summary>Derives a descriptor from a type's [DependsOn] attribute.</summary>
    public static ModuleDescriptor FromType(Type moduleType, string? name = null, bool optional = false)
        => new(name ?? moduleType.Name,
               moduleType,
               moduleType.GetCustomAttribute<DependsOnAttribute>()?.ModuleTypes,
               optional);
}
