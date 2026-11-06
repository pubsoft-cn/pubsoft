using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

public sealed class OutboundContractOptions
{
    public INamingPolicy NamingPolicy { get; set; } = CamelCaseNamingPolicy.Instance;
    public List<Assembly> Assemblies { get; } = [];
}
