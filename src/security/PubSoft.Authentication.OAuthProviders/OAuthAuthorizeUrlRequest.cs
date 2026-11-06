namespace PubSoft.OAuth;


/// <summary>Request for building an authorize URL.</summary>
public sealed class OAuthAuthorizeUrlRequest
{
    public string Provider { get; set; } = "";
    public string ChannelType { get; set; } = "";
    public string AppId { get; set; } = "";
    public string? RedirectUri { get; set; }
    public string? State { get; set; }
    public string? Scope { get; set; }

    /// <summary>PKCE code challenge (required by providers that mandate PKCE, e.g. Twitter).</summary>
    public string? CodeChallenge { get; set; }
}
