namespace PubSoft.OAuth;


/// <summary>Result of building an authorize URL.</summary>
public sealed class OAuthAuthorizeUrlResult
{
    /// <summary>Whether the URL was built.</summary>
    public bool Success { get; set; }
    /// <summary>The authorize URL to redirect the user to.</summary>
    public string AuthorizeUrl { get; set; } = "";
    /// <summary>The state value embedded in the URL (echo it back for CSRF check).</summary>
    public string? State { get; set; }
    /// <summary>The app id the URL was built for.</summary>
    public string? AppId { get; set; }
    /// <summary>Failure detail.</summary>
    public string? Message { get; set; }

    /// <summary>Creates a failure result.</summary>
    public static OAuthAuthorizeUrlResult Fail(string message) => new() { Success = false, Message = message };
}
