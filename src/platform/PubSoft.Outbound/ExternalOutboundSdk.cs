using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Contracts;

namespace PubSoft.Outbound;


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
