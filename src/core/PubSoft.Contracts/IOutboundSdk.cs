using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;

/// <summary>Thin typed facade over <see cref="IOutboundEngine"/> for SDK consumers.</summary>
public interface IOutboundSdk
{
    /// <summary>Executes an outbound request and returns the typed response.</summary>
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