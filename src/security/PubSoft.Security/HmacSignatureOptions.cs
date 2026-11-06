using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;


public sealed class HmacSignatureOptions
{
    public int MaxTimestampSkewSeconds { get; set; } = 300;
    public int MaxAppIdLength { get; set; } = 128;
    public int MaxNonceLength { get; set; } = 256;
}
