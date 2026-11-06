using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;

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
