namespace PubSoft.OAuth;


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
