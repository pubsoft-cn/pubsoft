using System.Diagnostics.CodeAnalysis;

namespace PubSoft.Contracts;

/// <summary>Provider that executes a family of outbound operations.</summary>
public interface IOutboundProvider
{
    /// <summary>The provider identifier.</summary>
    string ProviderName { get; }

    /// <summary>Whether this provider handles the operation.</summary>
    bool CanExecute(string operationId);

    /// <summary>Executes the operation and returns the raw response.</summary>
    Task<object> ExecuteAsync(
        string operationId,
        object request,
        OutboundProviderContext context,
        CancellationToken cancellationToken = default);
}
