namespace PubSoft.OAuth;


/// <summary>Routes a provider name to its <see cref="IOAuthProvider"/> implementation.</summary>
public interface IOAuthProviderRegistry
{
    IOAuthProvider? GetProvider(string provider);
    IReadOnlyList<IOAuthProvider> GetAllProviders();
}
