namespace PubSoft.OAuth;


/// <summary>Routes a provider name to its <see cref="IOAuthProvider"/> implementation.</summary>
public interface IOAuthProviderRegistry
{
    /// <summary>Resolves a provider by name, or null.</summary>
    IOAuthProvider? GetProvider(string provider);
    /// <summary>All registered providers.</summary>
    IReadOnlyList<IOAuthProvider> GetAllProviders();
}
