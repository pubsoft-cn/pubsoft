using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;


/// <summary>Executes outbound API operations against the registered providers.</summary>
public interface IOutboundEngine
{
    /// <summary>Executes an outbound request and returns the typed response.</summary>
    [RequiresUnreferencedCode("Contract mapping uses reflection over request members; preserve contract types under trimming.")]
    Task<TResponse> ExecuteAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TRequest, TResponse>(
TRequest request,
        string profileId,
        long tenantId,
        CancellationToken cancellationToken = default)
        where TRequest : IApiRequest<TResponse>
        where TResponse : notnull;
}
