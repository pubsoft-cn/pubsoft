namespace PubSoft.OAuth;


/// <summary>Request for building an authorize URL.</summary>
public sealed class OAuthAuthorizeUrlRequest
{
    /// <summary>Provider name (OAuthProviderNames).</summary>
    public string Provider { get; set; } = "";
    /// <summary>Channel sub-type (OAuthChannelTypes).</summary>
    public string ChannelType { get; set; } = "";
    /// <summary>App id to build the URL for.</summary>
    public string AppId { get; set; } = "";
    /// <summary>Redirect URI override; defaults to the channel config's.</summary>
    public string? RedirectUri { get; set; }
    /// <summary>State to embed; generated when omitted.</summary>
    public string? State { get; set; }
    /// <summary>Scope override; defaults to the provider's basic scope.</summary>
    public string? Scope { get; set; }

    /// <summary>PKCE code challenge (required by providers that mandate PKCE, e.g. Twitter).</summary>
    public string? CodeChallenge { get; set; }
}
