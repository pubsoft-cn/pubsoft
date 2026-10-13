using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;

public interface IOutboundSdk
{
    [RequiresUnreferencedCode("Contract mapping uses reflection over request members; preserve contract types under trimming.")]
    Task<TResponse> ExecuteAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TRequest, TResponse>(
        TRequest request,
        long tenantId,
        string profileId = "default",
        CancellationToken cancellationToken = default)
        where TRequest : IApiRequest<TResponse>
        where TResponse : notnull;
}