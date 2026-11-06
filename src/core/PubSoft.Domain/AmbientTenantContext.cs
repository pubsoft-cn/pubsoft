namespace PubSoft.Domain;

/// <summary>
/// <see cref="ITenantContext"/> implementation backed by async-local storage.
/// The ambient value flows with the logical async control flow; it is not global to the process.
/// </summary>
/// <remarks>
/// Register as a singleton (for example <c>services.AddSingleton&lt;ITenantContext, AmbientTenantContext&gt;()</c>);
/// ambient state is per control flow, so instance lifetime does not affect isolation.
/// A host request adapter is expected to call <see cref="Change"/> once per request/job from a trusted source;
/// this type does not validate the authority of the value being set.
/// </remarks>
public sealed class AmbientTenantContext : ITenantContext
{
    private static readonly AsyncLocal<State?> Current = new();

    public long? TenantId => Current.Value?.TenantId;

    /// <summary>
    /// Temporarily replaces the ambient tenant for the current async control flow.
    /// </summary>
    /// <param name="tenantId">Tenant to become current; <c>null</c> selects host (unscoped) context.</param>
    /// <returns>A scope that restores the previous tenant on dispose. Dispose scopes in reverse order of acquisition.</returns>
    public IDisposable Change(long? tenantId)
    {
        var previous = Current.Value;
        Current.Value = new State { TenantId = tenantId };
        return new RevertScope(() => Current.Value = previous);
    }

    private sealed class State
    {
        public long? TenantId;
    }

    private sealed class RevertScope(Action revert) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            revert();
        }
    }
}
