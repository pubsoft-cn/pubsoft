using System.Security.Cryptography;

namespace PubSoft.Cryptography;

/// <summary>A <see cref="CryptographicException"/> carrying the machine-readable failure reason and key id.</summary>
public sealed class PubSoftDecryptionException : CryptographicException
{
    /// <summary>Creates the exception.</summary>
    public PubSoftDecryptionException(string message, DecryptionFailureReason reason, string? keyId = null)
        : base(message)
    {
        Reason = reason;
        KeyId = keyId;
    }

    /// <summary>The classified failure cause.</summary>
    public DecryptionFailureReason Reason { get; }

    /// <summary>The key id embedded in the envelope, when known.</summary>
    public string? KeyId { get; }
}
