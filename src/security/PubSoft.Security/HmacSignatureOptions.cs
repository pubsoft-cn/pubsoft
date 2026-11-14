namespace PubSoft.Security;


/// <summary>Wire encoding of the supplied signature bytes.</summary>
public enum HmacSignatureEncoding : byte
{
    /// <summary>Lowercase hex string.</summary>
    LowerHex = 0,
    /// <summary>Standard Base64 -- matches <c>PubSoft.Cryptography.ClientSigner</c>.</summary>
    Base64 = 1
}

/// <summary>Epoch unit of the timestamp field.</summary>
public enum HmacTimestampUnit : byte
{
    /// <summary>Unix seconds.</summary>
    Seconds = 0,
    /// <summary>Unix milliseconds -- matches <c>PubSoft.Cryptography.ClientSigner</c>.</summary>
    Milliseconds = 1
}

/// <summary>Validation policy for <see cref="HmacSignatureValidator"/>.</summary>
public sealed class HmacSignatureOptions
{
    /// <summary>Allowed clock skew between signer and verifier.</summary>
    public int MaxTimestampSkewSeconds { get; set; } = 300;
    /// <summary>Rejects app ids longer than this before lookup - bounds the oracle surface.</summary>
    public int MaxAppIdLength { get; set; } = 128;
    /// <summary>Rejects nonces longer than this.</summary>
    public int MaxNonceLength { get; set; } = 256;

    /// <summary>Signature wire encoding. Defaults to <see cref="HmacSignatureEncoding.LowerHex"/>.</summary>
    public HmacSignatureEncoding SignatureEncoding { get; set; } = HmacSignatureEncoding.LowerHex;

    /// <summary>Timestamp epoch unit. Defaults to <see cref="HmacTimestampUnit.Seconds"/>.</summary>
    public HmacTimestampUnit TimestampUnit { get; set; } = HmacTimestampUnit.Seconds;
}
