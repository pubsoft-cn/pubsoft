using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.DependencyInjection;


/// <summary>
/// Overrides the service types a convention-scanned class is registered under.
/// When present, only the listed types (plus the concrete type itself) are registered --
/// use when a class implements interfaces that should not become DI entry points.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ExposeServicesAttribute : Attribute
{

    /// <summary>Creates the attribute for the given service types.</summary>
    public ExposeServicesAttribute(params Type[] serviceTypes)
    {
        ArgumentNullException.ThrowIfNull(serviceTypes);
        ServiceTypes = serviceTypes;
    }
    /// <summary>Service types exposed by the decorated class.</summary>
    public Type[] ServiceTypes { get; }
}
