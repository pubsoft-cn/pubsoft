namespace PubSoft.OAuth;


/// <summary>Result of a WeChat global access_token fetch (cgi-bin/token).</summary>
public sealed class WeChatAccessTokenResult
{
    /// <summary>Whether the fetch succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>The global access token.</summary>
    public string AccessToken { get; set; } = "";
    /// <summary>Failure detail.</summary>
    public string? Message { get; set; }

    /// <summary>Creates a success result.</summary>
    public static WeChatAccessTokenResult Ok(string token) => new() { Success = true, AccessToken = token };
    /// <summary>Creates a failure result.</summary>
    public static WeChatAccessTokenResult Fail(string msg) => new() { Success = false, Message = msg };
}
