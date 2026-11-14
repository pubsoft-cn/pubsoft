using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Messaging.Outbox;


/// <summary>Supplies DI scopes for outbox processing (per-tenant or host scope enumeration).</summary>
public interface IOutboxScopeProvider
{
    /// <summary>Enumerates the scopes to process under.</summary>
    IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(CancellationToken cancellationToken = default);
}
