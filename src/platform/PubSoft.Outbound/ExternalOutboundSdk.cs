using System.Diagnostics.CodeAnalysis;
using PubSoft.Contracts;

namespace PubSoft.Outbound;


/// <summary>External-facing facade over <see cref="IOutboundEngine"/>.</summary>
public sealed class ExternalOutboundSdk(IOutboundEngine engine) : IOutboundSdk
{
    /// <inheritdoc />
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
