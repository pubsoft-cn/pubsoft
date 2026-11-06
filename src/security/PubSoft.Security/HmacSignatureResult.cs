using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;

public sealed class HmacSignatureResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public string? AppId { get; init; }
}
