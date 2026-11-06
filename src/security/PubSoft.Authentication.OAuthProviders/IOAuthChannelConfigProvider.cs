namespace PubSoft.OAuth;


/// <summary>
/// Channel-config lookup implemented by the host (e.g. backed by a channel-accounts table).
/// The package depends on the contract only — never on a concrete credential store.
/// </summary>
public interface IOAuthChannelConfigProvider
{
    Task<OAuthChannelConfig?> GetByAppIdAsync(string appId, CancellationToken ct = default);
    Task<OAuthChannelConfig?> GetDefaultAsync(string provider, string channelType, CancellationToken ct = default);
    Task<List<OAuthChannelConfig>> ListByProviderAsync(string provider, CancellationToken ct = default);
    Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default);
}
