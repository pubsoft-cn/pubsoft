using PubSoft.Domain;

namespace PubSoft.Testing;

/// <summary>Immutable <see cref="ITenantContext"/> returning a fixed tenant for tests.</summary>
public sealed record FixedTenantContext(long? TenantId) : ITenantContext;

/// <summary>Immutable <see cref="IMemberScopeContext"/> returning a fixed member scope for tests.</summary>
public sealed record FixedMemberScopeContext(long? MemberId, bool IsMemberScoped = true) : IMemberScopeContext;

/// <summary>
/// Manually controlled <see cref="TimeProvider"/> for deterministic tests. Local time zone and
/// timestamp frequency fall through to the system provider; only UTC now is overridden.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _utcNow;
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_sync)
            _utcNow = utcNow;
    }

    public void Advance(TimeSpan delta)
    {
        lock (_sync)
            _utcNow += delta;
    }
}

/// <summary><see cref="IPubSoftSaveParticipant"/> delegating to a callback, for wiring tests.</summary>
public sealed class DelegateSaveParticipant(Func<CancellationToken, Task<int>> save) : IPubSoftSaveParticipant
{
    private readonly Func<CancellationToken, Task<int>> _save =
        save ?? throw new ArgumentNullException(nameof(save));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _save(cancellationToken);
}
