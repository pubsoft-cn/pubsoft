namespace PubSoft.Cryptography;

/// <summary>Provides the active encryption key and resolves keys by identifier.</summary>
/// <remarks>Keys must be 32-byte AES-256 keys supplied by an external secret store.</remarks>
public interface IEncryptionKeyRing
{
    string ActiveKeyId { get; }

    bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key);
}