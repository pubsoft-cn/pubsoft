namespace PubSoft.OAuth;

/// <summary>Result of a code exchange (code → openId/userId).</summary>
public sealed class OAuthCodeExchangeResult
{
    public bool Success { get; set; }
    public string OpenId { get; set; } = "";
    public string? UnionId { get; set; }
    public string? UserId { get; set; }
    public string? NickName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? SessionKey { get; set; }
    public string? Message { get; set; }

    public static OAuthCodeExchangeResult Fail(string message) => new() { Success = false, Message = message };
    public static OAuthCodeExchangeResult Ok(string openId) => new() { Success = true, OpenId = openId };
}

/// <summary>Result of a user-info query.</summary>
public sealed class OAuthUserInfoResult
{
    public bool Success { get; set; }
    public string OpenId { get; set; } = "";
    public string? UnionId { get; set; }
    public string? UserId { get; set; }
    public string? NickName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Gender { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public string? Message { get; set; }

    public static OAuthUserInfoResult Fail(string message) => new() { Success = false, Message = message };
}

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

/// <summary>Result of building an authorize URL.</summary>
public sealed class OAuthAuthorizeUrlResult
{
    public bool Success { get; set; }
    public string AuthorizeUrl { get; set; } = "";
    public string? State { get; set; }
    public string? AppId { get; set; }
    public string? Message { get; set; }

    public static OAuthAuthorizeUrlResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>Result of a phone-number query (WeChat/Alipay mini programs).</summary>
public sealed class OAuthPhoneResult
{
    public bool Success { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PurePhoneNumber { get; set; }
    public string? CountryCode { get; set; }
    public string? Message { get; set; }

    public static OAuthPhoneResult Fail(string message) => new() { Success = false, Message = message };
}
