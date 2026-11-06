namespace PubSoft.OAuth;


/// <summary>
/// Channel credential/config record consumed by providers. The host supplies these via
/// <see cref="IOAuthChannelConfigProvider"/> (typically DB-driven); this package never reads them from config itself.
/// </summary>
public sealed class OAuthChannelConfig
{
    /// <summary>Channel record id.</summary>
    public long Id { get; set; }
    /// <summary>Provider name (OAuthProviderNames).</summary>
    public string Provider { get; set; } = "";
    /// <summary>Channel sub-type (OAuthChannelTypes).</summary>
    public string ChannelType { get; set; } = "";
    /// <summary>Display name for admin UI.</summary>
    public string Name { get; set; } = "";
    /// <summary>Provider-issued app id.</summary>
    public string AppId { get; set; } = "";
    /// <summary>Provider-issued secret - decrypt before handing to providers.</summary>
    public string? AppSecret { get; set; }
    /// <summary>OAuth redirect URI.</summary>
    public string? RedirectUri { get; set; }
    /// <summary>Default channel for its provider+type pair.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Provider-specific extra settings JSON (Alipay RSA keys, WeChat UnionId merging, ...).</summary>
    public string? ExtraJson { get; set; }
}
