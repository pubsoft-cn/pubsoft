namespace PubSoft.OAuth;


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
