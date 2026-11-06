namespace PubSoft.OAuth;

/// <summary>
/// Unified OAuth provider contract. All channel implementations (WeChat/Alipay/WeCom/DingTalk/QQ/GitHub/...)
/// implement this interface. Two flows are supported:
/// 1. direct code→openId exchange (mini programs): <see cref="ExchangeCodeAsync"/>
/// 2. standard OAuth 2.0 web flow (H5/QR login): <see cref="GetAuthorizeUrlAsync"/> + <see cref="ExchangeCodeAsync"/>
/// </summary>
public interface IOAuthProvider
{
    /// <summary>Provider identifier (e.g. "WeChat", "Alipay"). See <see cref="OAuthProviderNames"/>.</summary>
    string Provider { get; }

    /// <summary>Whether the provider supports the given channel type (see <see cref="OAuthChannelTypes"/>).</summary>
    bool SupportsChannelType(string channelType);

    /// <summary>
    /// Builds the OAuth authorize URL (H5/QR web flow).
    /// Providers without a redirect flow may throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default);

    /// <summary>Exchanges an auth code for the user identity (mini-program direct calls and web-flow callbacks).</summary>
    Task<OAuthCodeExchangeResult> ExchangeCodeAsync(string appId, string code, string? channelType = null, CancellationToken ct = default);

    /// <summary>Fetches user profile information (optional; some providers do not support it).</summary>
    Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default);

    /// <summary>Fetches a phone number (optional; WeChat/Alipay mini-program flows).</summary>
    Task<OAuthPhoneResult> GetPhoneNumberAsync(string appId, string code, CancellationToken ct = default);
}
