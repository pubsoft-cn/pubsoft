using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


/// <summary>Supplies DI scopes for outbox processing (per-tenant or host scope enumeration).</summary>
public interface IOutboxScopeProvider
{
    /// <summary>Enumerates the scopes to process under.</summary>
    IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(CancellationToken cancellationToken = default);
}
