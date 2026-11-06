namespace PubSoft.OAuth;


/// <summary>Result of a user-info query.</summary>
public sealed class OAuthUserInfoResult
{
    /// <summary>Whether the query succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Provider-scoped user id (openid).</summary>
    public string OpenId { get; set; } = "";
    /// <summary>Cross-app user id when the provider issues one.</summary>
    public string? UnionId { get; set; }
    /// <summary>Provider-side numeric user id when distinct from openid.</summary>
    public string? UserId { get; set; }
    /// <summary>Display nickname.</summary>
    public string? NickName { get; set; }
    /// <summary>Avatar URL.</summary>
    public string? AvatarUrl { get; set; }
    /// <summary>Email when the provider returns one.</summary>
    public string? Email { get; set; }
    /// <summary>Phone number when returned.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Provider-reported gender.</summary>
    public string? Gender { get; set; }
    /// <summary>Provider-reported province.</summary>
    public string? Province { get; set; }
    /// <summary>Provider-reported city.</summary>
    public string? City { get; set; }
    /// <summary>Failure or diagnostic detail.</summary>
    public string? Message { get; set; }

    /// <summary>Creates a failure result.</summary>
    public static OAuthUserInfoResult Fail(string message) => new() { Success = false, Message = message };
}
