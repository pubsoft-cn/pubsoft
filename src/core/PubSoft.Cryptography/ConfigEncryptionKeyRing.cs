namespace PubSoft.Cryptography;

/// <summary>Options for <see cref="ConfigEncryptionKeyRing"/>. Keys are base64-encoded 32-byte AES keys.</summary>
public sealed class EncryptionKeyRingOptions
{
    /// <summary>Identifier of the key used for new encryptions. Must exist in <see cref="Keys"/>.</summary>
    public required string ActiveKeyId { get; set; }
    /// <summary>Key id -> base64 key material. All keys remain registered for decryption after rotation.</summary>
    public required IReadOnlyDictionary<string, string> Keys { get; set; }
}

/// <summary>
/// Configuration-backed <see cref="IEncryptionKeyRing"/> for hosts that source key material from
/// settings/secret stores rather than a dedicated KMS. Keys must be 32 bytes when base64-decoded;
/// the ring fails closed (decrypt-by-id misses, encrypt throws) on malformed material.
/// </summary>
public sealed class ConfigEncryptionKeyRing : IEncryptionKeyRing
{
    private readonly Dictionary<string, byte[]> _keys;

    /// <summary>Creates a key ring from configured keys; fails closed when the active key id is unset or missing.</summary>
    public ConfigEncryptionKeyRing(EncryptionKeyRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ActiveKeyId);
        _keys = [];
        foreach (var (id, material) in options.Keys)
        {
            byte[] key;
            try
            {
                key = Convert.FromBase64String(material);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"Encryption key '{id}' is not valid base64.", nameof(options), ex);
            }
            if (key.Length != 32)
                throw new ArgumentException($"Encryption key '{id}' must decode to 32 bytes, got {key.Length}.", nameof(options));
            _keys[id] = key;
        }
        if (!_keys.ContainsKey(options.ActiveKeyId))
            throw new ArgumentException($"ActiveKeyId '{options.ActiveKeyId}' has no entry in Keys.", nameof(options));
        ActiveKeyId = options.ActiveKeyId;
    }

    /// <inheritdoc />
    public string ActiveKeyId { get; }

    /// <inheritdoc />
    public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
    {
        if (_keys.TryGetValue(keyId, out var found))
        {
            key = found;
            return true;
        }
        key = ReadOnlyMemory<byte>.Empty;
        return false;
    }
}
