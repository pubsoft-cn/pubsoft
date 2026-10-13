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

    private sealed class State
    {
        public long? TenantId;
    }

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

/// <summary>
/// <see cref="IMemberScopeContext"/> implementation backed by async-local storage.
/// </summary>
/// <remarks>
/// Setting <see cref="IsMemberScoped"/> to <c>true</c> without a <see cref="MemberId"/>
/// is a valid state: scoped query filters fail closed in that configuration rather than
/// silently widening results. Register as a singleton; ambient state is per control flow.
/// </remarks>
public sealed class AmbientMemberScopeContext : IMemberScopeContext
{
    private static readonly AsyncLocal<State?> Current = new();

    private sealed class State
    {
        public long? MemberId;
        public bool IsMemberScoped;
    }

    public long? MemberId => Current.Value?.MemberId;

    public bool IsMemberScoped => Current.Value?.IsMemberScoped == true;

    /// <summary>
    /// Temporarily replaces the ambient member scope for the current async control flow.
    /// </summary>
    /// <param name="memberId">Member to become current.</param>
    /// <param name="isMemberScoped">
    /// Whether member scoping is active. When <c>false</c> the member filter is inert
    /// even if <paramref name="memberId"/> is provided.
    /// </param>
    /// <returns>A scope that restores the previous member scope on dispose. Dispose scopes in reverse order of acquisition.</returns>
    public IDisposable Change(long? memberId, bool isMemberScoped = true)
    {
        var previous = Current.Value;
        Current.Value = new State { MemberId = memberId, IsMemberScoped = isMemberScoped };
        return new RevertScope(() => Current.Value = previous);
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
