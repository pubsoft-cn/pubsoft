namespace PubSoft.Modularity;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DependsOnAttribute : Attribute
{
    public DependsOnAttribute(params Type[] moduleTypes) => ModuleTypes = moduleTypes;

    public Type[] ModuleTypes { get; }
}
