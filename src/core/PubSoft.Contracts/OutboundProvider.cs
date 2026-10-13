using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;

public sealed class OutboundProviderContext
{
    public string ProfileId { get; set; } = string.Empty;
    public long TenantId { get; set; }
    public string OperationId { get; set; } = string.Empty;
}

public interface IOutboundProvider
{
    string ProviderName { get; }

    bool CanExecute(string operationId);

    Task<object> ExecuteAsync(
        string operationId,
        object request,
        OutboundProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IOutboundEngine
{
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