namespace PubSoft.OAuth;


/// <summary>Result of a phone-number query (WeChat/Alipay mini programs).</summary>
public sealed class OAuthPhoneResult
{
    /// <summary>Whether the query succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Full phone number including country code.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Phone number without country code.</summary>
    public string? PurePhoneNumber { get; set; }
    /// <summary>Country dialing code.</summary>
    public string? CountryCode { get; set; }
    /// <summary>Failure detail.</summary>
    public string? Message { get; set; }

    /// <summary>Creates a failure result.</summary>
    public static OAuthPhoneResult Fail(string message) => new() { Success = false, Message = message };
}
