using System.Security.Cryptography;

namespace PubSoft.Cryptography;

/// <summary>Machine-readable cause of a failed <see cref="AesGcmEncryptor.Decrypt"/> call.</summary>
public enum DecryptionFailureReason
{
    /// <summary>The envelope is malformed: wrong prefix, field count, length, or base64.</summary>
    MalformedEnvelope,
    /// <summary>The key identifier embedded in the envelope is itself invalid.</summary>
    InvalidKeyId,
    /// <summary>The key identifier is not present in the configured key ring.</summary>
    UnknownKeyId,
    /// <summary>The resolved key material is not exactly 32 bytes.</summary>
    InvalidKeyLength,
    /// <summary>
    /// GCM authentication failed - the key material does not match the ciphertext,
    /// or the additional authenticated data used at encryption does not match.
    /// </summary>
    AuthenticationFailed,
}

