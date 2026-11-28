using PubSoft.Domain;

namespace PubSoft.Messaging.Outbox;

/// <summary>
/// Initializes <see cref="ScopedTenantContext"/> inside each tenant scope. The context must be
/// registered scoped and the same instance must be exposed as <see cref="ITenantContext"/>; ambient
/// async-local context is intentionally not used because it does not reliably flow from an async
/// enumerator producer to the worker consuming the yielded scope.
/// </summary>
public sealed class ScopedTenantOutboxScopeInitializer : IOutboxTenantScopeInitializer
{
    /// <inheritdoc />
    public Task InitializeAsync(IServiceProvider serviceProvider, long? tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        cancellationToken.ThrowIfCancellationRequested();
        if (serviceProvider.GetService(typeof(ITenantContext)) is not ScopedTenantContext tenantContext)
            throw new InvalidOperationException(
                "Tenant outbox enumeration requires ITenantContext to resolve to ScopedTenantContext inside the processing scope.");
        tenantContext.Initialize(tenantId);
        return Task.CompletedTask;
    }
}
