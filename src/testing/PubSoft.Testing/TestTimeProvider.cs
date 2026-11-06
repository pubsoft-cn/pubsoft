using PubSoft.Domain;

namespace PubSoft.Testing;


/// <summary>
/// Manually controlled <see cref="TimeProvider"/> for deterministic tests. Local time zone and
/// timestamp frequency fall through to the system provider; only UTC now is overridden.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _utcNow;

    /// <summary>Creates the provider at a fixed instant.</summary>
    public TestTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _utcNow;
    }

    /// <summary>Moves the clock to a fixed instant.</summary>
    public void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_sync)
            _utcNow = utcNow;
    }

    /// <summary>Advances the clock.</summary>
    public void Advance(TimeSpan delta)
    {
        lock (_sync)
            _utcNow += delta;
    }
}
