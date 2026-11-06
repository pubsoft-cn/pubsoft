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
