using System.Reflection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

/// <summary>Contract registration options.</summary>
public sealed class OutboundContractOptions
{
    /// <summary>Field naming policy (default camelCase).</summary>
    public INamingPolicy NamingPolicy { get; set; } = CamelCaseNamingPolicy.Instance;
    /// <summary>Assemblies to scan for contract types.</summary>
    public List<Assembly> Assemblies { get; } = [];
}
