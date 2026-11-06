using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;


public static class HmacSignatureHeaders
{
    public const string AppId = "X-Fend-App-Id";
    public const string Signature = "X-Fend-Signature";
    public const string Timestamp = "X-Fend-Timestamp";
    public const string Nonce = "X-Fend-Nonce";
}
