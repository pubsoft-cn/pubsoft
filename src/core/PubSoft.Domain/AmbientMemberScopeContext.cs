namespace PubSoft.Domain;


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

    private sealed class State
    {
        public long? MemberId;
        public bool IsMemberScoped;
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
