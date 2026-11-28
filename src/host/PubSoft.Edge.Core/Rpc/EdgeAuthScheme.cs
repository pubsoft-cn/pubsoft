using System.Text.Json.Serialization;

namespace PubSoft.Edge;

/// <summary>
/// Edge credential scheme -- how <see cref="EdgeRpcRequest.Credential"/> is interpreted.
///
/// Default <see cref="Hmac"/> (HMAC-SHA256). New integrations use Hmac; <see cref="Md5"/>
/// exists for clients that cannot move off MD5 signing (e.g. embedded devices).
/// </summary>
public enum EdgeAuthScheme
{
    /// <summary>HMAC-SHA256 signature (default) -- third-party apps/devices, secretKey + canonical signing string.</summary>
    [JsonStringEnumMemberName("hmac")]
    Hmac = 0,

    /// <summary>sessionKey -- front-end clients (mini-program/H5) after login; short token replacing verbose JWT.</summary>
    [JsonStringEnumMemberName("session")]
    Session = 1,

    /// <summary>No authentication -- e.g. login endpoints.</summary>
    [JsonStringEnumMemberName("none")]
    None = 2,

    /// <summary>MD5 signature -- legacy device/client compatibility; same canonical payload as Hmac.</summary>
    [JsonStringEnumMemberName("md5")]
    Md5 = 3,
}
