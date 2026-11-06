using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;


/// <summary>Default HMAC signature header names -- hosts may override via HmacValidationOptions.</summary>
public static class HmacSignatureHeaders
{
    /// <summary>Header carrying the app/client identifier.</summary>
    public const string AppId = "X-App-Id";
    /// <summary>Header carrying the request signature.</summary>
    public const string Signature = "X-Signature";
    /// <summary>Header carrying the signing timestamp.</summary>
    public const string Timestamp = "X-Timestamp";
    /// <summary>Header carrying the per-request nonce.</summary>
    public const string Nonce = "X-Nonce";
}
