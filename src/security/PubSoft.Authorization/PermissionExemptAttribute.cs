using System.Text;

namespace PubSoft.Authorization;


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class PermissionExemptAttribute : Attribute;
