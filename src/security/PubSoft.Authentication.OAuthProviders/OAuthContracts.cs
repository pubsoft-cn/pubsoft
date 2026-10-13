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

/// <summary>Routes a provider name to its <see cref="IOAuthProvider"/> implementation.</summary>
public interface IOAuthProviderRegistry
{
    IOAuthProvider? GetProvider(string provider);
    IReadOnlyList<IOAuthProvider> GetAllProviders();
}

/// <summary>
/// Channel credential/config record consumed by providers. The host supplies these via
/// <see cref="IOAuthChannelConfigProvider"/> (typically DB-driven); this package never reads them from config itself.
/// </summary>
public sealed class OAuthChannelConfig
{
    public long Id { get; set; }
    public string Provider { get; set; } = "";
    public string ChannelType { get; set; } = "";
    public string Name { get; set; } = "";
    public string AppId { get; set; } = "";
    public string? AppSecret { get; set; }
    public string? RedirectUri { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>Provider-specific extra settings JSON (Alipay RSA keys, WeChat UnionId merging, ...).</summary>
    public string? ExtraJson { get; set; }
}

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

/// <summary>Built-in provider name constants.</summary>
public static class OAuthProviderNames
{
    public const string WeChat = "WeChat";
    public const string WeCom = "WeCom";
    public const string Alipay = "Alipay";
    public const string DingTalk = "DingTalk";
    public const string QQ = "QQ";
    public const string Douyin = "Douyin";
    public const string Toutiao = "Toutiao";
    public const string Taobao = "Taobao";
    public const string Huawei = "Huawei";
    public const string Xiaomi = "Xiaomi";
    public const string GitHub = "GitHub";
    public const string Gitee = "Gitee";
    public const string Baidu = "Baidu";
    public const string Weibo = "Weibo";
    public const string Apple = "Apple";
    public const string Microsoft = "Microsoft";
    public const string Google = "Google";
    public const string GitLab = "GitLab";
    public const string Twitter = "Twitter";
    public const string Custom = "Custom";

    /// <summary>All built-in providers.</summary>
    public static readonly string[] BuiltIn =
    [
        WeChat, WeCom, Alipay, DingTalk, QQ,
        Douyin, Toutiao, Taobao, Huawei, Xiaomi,
        GitHub, Gitee, Baidu, Weibo, Apple,
        Microsoft, Google, GitLab, Twitter,
    ];

    /// <summary>Whether the name is a known built-in provider (case-insensitive).</summary>
    public static bool IsKnown(string provider) =>
        BuiltIn.Any(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Channel sub-type constants.</summary>
public static class OAuthChannelTypes
{
    public const string OfficialAccount = "OfficialAccount";
    public const string MiniProgram = "MiniProgram";
    public const string OpenPlatform = "OpenPlatform";
    public const string Web = "Web";
}
