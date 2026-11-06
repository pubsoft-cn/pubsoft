namespace PubSoft.OAuth;


/// <summary>
/// Channel-config lookup implemented by the host (e.g. backed by a channel-accounts table).
/// The package depends on the contract only -- never on a concrete credential store.
/// </summary>
public interface IOAuthChannelConfigProvider
{
    /// <summary>Finds the channel owning the app id.</summary>
    Task<OAuthChannelConfig?> GetByAppIdAsync(string appId, CancellationToken ct = default);
    /// <summary>Finds the default channel for a provider+type pair.</summary>
    Task<OAuthChannelConfig?> GetDefaultAsync(string provider, string channelType, CancellationToken ct = default);
    /// <summary>Lists all channels for a provider.</summary>
    Task<List<OAuthChannelConfig>> ListByProviderAsync(string provider, CancellationToken ct = default);
    /// <summary>Lists all channels.</summary>
    Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default);
}
