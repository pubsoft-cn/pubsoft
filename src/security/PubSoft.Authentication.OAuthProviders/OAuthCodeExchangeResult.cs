namespace PubSoft.OAuth;

/// <summary>Result of a code exchange (code -> openId/userId).</summary>
public sealed class OAuthCodeExchangeResult
{
    /// <summary>Whether the exchange succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Provider-scoped user id.</summary>
    public string OpenId { get; set; } = "";
    /// <summary>Cross-app user id when issued.</summary>
    public string? UnionId { get; set; }
    /// <summary>Provider-side numeric user id when distinct from openid.</summary>
    public string? UserId { get; set; }
    /// <summary>Display nickname.</summary>
    public string? NickName { get; set; }
    /// <summary>Avatar URL.</summary>
    public string? AvatarUrl { get; set; }
    /// <summary>User access token when the flow returns one.</summary>
    public string? AccessToken { get; set; }
    /// <summary>Refresh token when issued.</summary>
    public string? RefreshToken { get; set; }
    /// <summary>Session key for providers that issue one (e.g. WeChat miniprogram).</summary>
    public string? SessionKey { get; set; }
    /// <summary>Failure or diagnostic detail.</summary>
    public string? Message { get; set; }

    /// <summary>Creates a failure result.</summary>
    public static OAuthCodeExchangeResult Fail(string message) => new() { Success = false, Message = message };
    /// <summary>Creates a success result.</summary>
    public static OAuthCodeExchangeResult Ok(string openId) => new() { Success = true, OpenId = openId };
}
