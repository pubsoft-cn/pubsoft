namespace PubSoft.Modularity;

/// <summary>Declares module dependencies.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DependsOnAttribute : Attribute
{
    /// <summary>Declares module types this module needs initialized first.</summary>
    public DependsOnAttribute(params Type[] moduleTypes) => ModuleTypes = moduleTypes;

    /// <summary>Dependency module types.</summary>
    public Type[] ModuleTypes { get; }
}
