namespace PubSoft.OAuth;

/// <summary>Default registry — routes provider names to implementations (case-insensitive).</summary>
public sealed class OAuthProviderRegistry : IOAuthProviderRegistry
{
    private readonly Dictionary<string, IOAuthProvider> _providers;

    public OAuthProviderRegistry(IEnumerable<IOAuthProvider> providers)
    {
        _providers = new Dictionary<string, IOAuthProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
            _providers[p.Provider] = p;
    }

    public IOAuthProvider? GetProvider(string provider) =>
        _providers.TryGetValue(provider, out var p) ? p : null;

    public IReadOnlyList<IOAuthProvider> GetAllProviders() => _providers.Values.ToList();
}
