namespace PubSoft.Domain;

/// <summary>
/// Mutable tenant context intended for a single DI scope/unit of work. Unlike
/// <see cref="AmbientTenantContext"/>, the value is stored on the scoped instance, so background
/// worker enumeration can initialize a scope before handing it to a different async control flow.
/// </summary>
/// <remarks>
/// Register once per scope and expose the same instance as <see cref="ITenantContext"/>. The host
/// remains responsible for deciding which identities may select a tenant; this type only stores the
/// trusted selection. Initialization is one-shot so a tenant cannot be accidentally switched after
/// tenant-aware services have started reading the scope.
/// </remarks>
public sealed class ScopedTenantContext : ITenantContext
{
    private long? _tenantId;
    private bool _initialized;

    /// <inheritdoc />
    public long? TenantId => _tenantId;

    /// <summary>Initializes this scope exactly once; null selects host (unscoped) context.</summary>
    public void Initialize(long? tenantId)
    {
        if (_initialized)
            throw new InvalidOperationException("The scoped tenant context has already been initialized.");
        _tenantId = tenantId;
        _initialized = true;
    }
}
