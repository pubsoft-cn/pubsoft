using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Cryptography;

/// <summary>Encrypts UTF-8 strings using a versioned AES-256-GCM envelope.</summary>
/// <remarks>
/// Envelope format: <c>pubsoft:v1:{keyId}:{nonceBase64}:{ciphertextBase64}:{tagBase64}</c>.
/// Key material is provided by <see cref="IEncryptionKeyRing"/> and is never derived from a password.
/// </remarks>
public sealed class AesGcmEncryptor
{
    private const string EnvelopePrefix = "pubsoft:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaximumPlaintextBytes = 16 * 1024 * 1024;
    private const int MaximumBase64PayloadLength = ((MaximumPlaintextBytes + 2) / 3) * 4;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly IEncryptionKeyRing _keyRing;

    public AesGcmEncryptor(IEncryptionKeyRing keyRing)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
    }

    /// <summary>Encrypts a string and returns a self-describing, authenticated envelope.</summary>
    public string Encrypt(string plaintext, ReadOnlySpan<byte> additionalAuthenticatedData = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var keyId = _keyRing.ActiveKeyId;
        ValidateKeyId(keyId);
        var plaintextBytes = StrictUtf8.GetBytes(plaintext);
        if (plaintextBytes.Length > MaximumPlaintextBytes)
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            throw new ArgumentOutOfRangeException(nameof(plaintext), "Plaintext exceeds the 16 MiB limit.");
        }

        byte[]? key = null;
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? associatedData = null;

        try
        {
            key = ResolveKey(keyId);
            nonce = RandomNumberGenerator.GetBytes(NonceSize);
            ciphertext = new byte[plaintextBytes.Length];
            tag = new byte[TagSize];
            associatedData = CreateAssociatedData(keyId, additionalAuthenticatedData);

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);

            return string.Join(':',
                "pubsoft",
                "v1",
                keyId,
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            Clear(key);
            Clear(nonce);
            Clear(ciphertext);
            Clear(tag);
            Clear(associatedData);
        }
    }

    /// <summary>Authenticates and decrypts a versioned envelope using the key identified in it.</summary>
    public string Decrypt(string envelope, ReadOnlySpan<byte> additionalAuthenticatedData = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Length > MaximumBase64PayloadLength + 128)
            throw InvalidEnvelope();

        var parts = envelope.Split(':');
        if (parts.Length != 6 || parts[0] != "pubsoft" || parts[1] != "v1")
            throw InvalidEnvelope();

        var keyId = parts[2];
        ValidateKeyId(keyId);
        if (parts[4].Length > MaximumBase64PayloadLength)
            throw InvalidEnvelope();

        var key = ResolveKey(keyId);
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? plaintext = null;
        byte[]? associatedData = null;
        try
        {
            nonce = ParseBase64(parts[3]);
            ciphertext = ParseBase64(parts[4]);
            tag = ParseBase64(parts[5]);
            if (nonce.Length != NonceSize || tag.Length != TagSize || ciphertext.Length > MaximumPlaintextBytes)
                throw InvalidEnvelope();

            plaintext = new byte[ciphertext.Length];
            associatedData = CreateAssociatedData(keyId, additionalAuthenticatedData);

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return StrictUtf8.GetString(plaintext);
        }
        catch (FormatException)
        {
            throw InvalidEnvelope();
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("The encrypted value could not be authenticated.");
        }
        catch (DecoderFallbackException)
        {
            throw InvalidEnvelope();
        }
        finally
        {
            Clear(key);
            Clear(nonce);
            Clear(ciphertext);
            Clear(tag);
            Clear(plaintext);
            Clear(associatedData);
        }
    }

    private byte[] ResolveKey(string keyId)
    {
        if (!_keyRing.TryGetKey(keyId, out var key))
            throw new CryptographicException($"Encryption key '{keyId}' is not available.");

        var keyBytes = key.ToArray();
        if (keyBytes.Length == KeySize)
            return keyBytes;

        CryptographicOperations.ZeroMemory(keyBytes);
        throw new CryptographicException("Encryption keys must contain exactly 32 bytes.");
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrEmpty(keyId) || keyId.Length > 64 ||
            keyId.Any(character => !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new CryptographicException("The encryption key identifier is invalid.");
    }

    private static byte[] ParseBase64(string value)
    {
        var bytes = Convert.FromBase64String(value);
        if (!string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw InvalidEnvelope();
        }

        return bytes;
    }

    private static byte[] CreateAssociatedData(string keyId, ReadOnlySpan<byte> additionalAuthenticatedData)
    {
        var prefix = Encoding.UTF8.GetBytes(EnvelopePrefix + keyId + "\0");
        var result = new byte[prefix.Length + additionalAuthenticatedData.Length];
        prefix.CopyTo(result, 0);
        additionalAuthenticatedData.CopyTo(result.AsSpan(prefix.Length));
        CryptographicOperations.ZeroMemory(prefix);
        return result;
    }

    private static void Clear(byte[]? bytes)
    {
        if (bytes is not null)
            CryptographicOperations.ZeroMemory(bytes);
    }

    private static CryptographicException InvalidEnvelope() => new("The encrypted value has an invalid format.");
}