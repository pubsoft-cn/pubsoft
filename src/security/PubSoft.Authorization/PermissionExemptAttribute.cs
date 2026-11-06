using System.Text;

namespace PubSoft.Authorization;


/// <summary>Marks a controller/action as exempt from permission enforcement.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class PermissionExemptAttribute : Attribute;
