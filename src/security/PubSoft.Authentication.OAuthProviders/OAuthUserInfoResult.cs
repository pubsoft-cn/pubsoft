namespace PubSoft.OAuth;


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
