namespace PubSoft.Cryptography;

/// <summary>Options for <see cref="ConfigEncryptionKeyRing"/>. Keys are base64-encoded 32-byte AES keys.</summary>
public sealed class EncryptionKeyRingOptions
{
    /// <summary>Identifier of the key used for new encryptions. Must exist in <see cref="Keys"/>.</summary>
    public required string ActiveKeyId { get; set; }
    /// <summary>Key id -> base64 key material. All keys remain registered for decryption after rotation.</summary>
    public required IReadOnlyDictionary<string, string> Keys { get; set; }
}
