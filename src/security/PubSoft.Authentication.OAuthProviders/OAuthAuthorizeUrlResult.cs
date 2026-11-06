namespace PubSoft.OAuth;


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
