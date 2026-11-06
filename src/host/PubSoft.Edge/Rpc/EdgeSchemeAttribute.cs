using System.Text.Json;

namespace PubSoft.Edge;


/// <summary>
/// Declares which credential schemes a handler accepts — attribute form, takes precedence over the property.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class EdgeSchemeAttribute(params EdgeAuthScheme[] schemes) : Attribute
{
    public EdgeAuthScheme[] Schemes { get; } = schemes;
}
