namespace PubSoft.Messaging.Outbox;

/// <summary>Initializes a newly created outbox processing scope for one tenant (or host when null).</summary>
public interface IOutboxTenantScopeInitializer
{
    /// <summary>Initializes scoped services before the scope is yielded to the worker.</summary>
    Task InitializeAsync(IServiceProvider serviceProvider, long? tenantId, CancellationToken cancellationToken);
}
