using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;

public sealed class OutboundContractOptions
{
    public INamingPolicy NamingPolicy { get; set; } = CamelCaseNamingPolicy.Instance;
    public List<Assembly> Assemblies { get; } = [];
}

public static class OutboundServiceCollectionExtensions
{
    [RequiresUnreferencedCode("Contract registration scans assemblies for contract types that may be trimmed.")]
    public static IServiceCollection AddOutboundContracts(
        this IServiceCollection services,
        Action<OutboundContractOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new OutboundContractOptions();
        configure(options);
        ArgumentNullException.ThrowIfNull(options.NamingPolicy);
        var assemblies = options.Assemblies.Distinct().ToArray();

        services.AddSingleton<IOutboundContractRegistry>(_ =>
        {
            var registry = new OutboundContractRegistry(options.NamingPolicy);
            foreach (var assembly in assemblies)
                registry.ScanAssembly(assembly);
            return registry;
        });
        services.AddSingleton<IOutboundEngine, OutboundEngine>();
        return services;
    }

    public static IServiceCollection AddExternalOutboundSdk(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IOutboundSdk, ExternalOutboundSdk>();
        return services;
    }
}

public sealed class ExternalOutboundSdk(IOutboundEngine engine) : IOutboundSdk
{
    [RequiresUnreferencedCode("Contract mapping uses reflection over request members; preserve contract types under trimming.")]
    public Task<TResponse> ExecuteAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TRequest, TResponse>(
        TRequest request,
        long tenantId,
        string profileId = "default",
        CancellationToken cancellationToken = default)
        where TRequest : IApiRequest<TResponse>
        where TResponse : notnull
        => engine.ExecuteAsync<TRequest, TResponse>(request, profileId, tenantId, cancellationToken);
}