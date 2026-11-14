namespace PubSoft.Security;

/// <summary>Outcome of one signature validation.</summary>
public sealed class HmacSignatureResult
{
    /// <summary>Whether the request is authentic.</summary>
    public bool IsValid { get; init; }
    /// <summary>Machine-readable failure code (signature_mismatch, timestamp_expired, nonce_replayed, ...).</summary>
    public string? Error { get; init; }
    /// <summary>The verified app id on success.</summary>
    public string? AppId { get; init; }
}
