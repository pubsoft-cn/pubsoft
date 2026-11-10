using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Cryptography;


/// <summary>Writes credentials with AEAD and optionally migrates legacy AES-CBC values on read.</summary>
/// <remarks>
/// The legacy key is mandatory and must be supplied from the original secret source. Legacy CBC values
/// are unauthenticated; use this migration path only for trusted stored data and persist replacement
/// ciphertext transactionally at the call site. No legacy ciphertext is ever written by this service.
/// </remarks>
public sealed class CredentialEncryptionService : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly AesGcmEncryptor _encryptor;
    private readonly byte[] _legacyKey;
    private bool _disposed;

    /// <summary>Creates the service; <paramref name="legacyKeyMaterial"/> decrypts pre-ring ciphertexts so they can be flagged for re-encryption.</summary>
    public CredentialEncryptionService(AesGcmEncryptor encryptor, string legacyKeyMaterial)
    {
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyKeyMaterial);
        var keyMaterialBytes = Encoding.UTF8.GetBytes(legacyKeyMaterial);
        try
        {
            _legacyKey = SHA256.HashData(keyMaterialBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterialBytes);
        }
    }

    /// <summary>Encrypts with the active ring key, binding optional AAD (e.g. column context) into the ciphertext.</summary>
    public string Encrypt(string plaintext, ReadOnlySpan<byte> additionalAuthenticatedData = default)
    {
        ThrowIfDisposed();
        return _encryptor.Encrypt(plaintext, additionalAuthenticatedData);
    }

    /// <summary>Decrypts via ring-or-legacy fallback; returns <see cref="DecryptedCredential.RequiresReEncryption"/> when the value used the legacy path.</summary>
    public DecryptedCredential Decrypt(string ciphertext, ReadOnlySpan<byte> additionalAuthenticatedData = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.StartsWith("pubsoft:", StringComparison.Ordinal))
            return new DecryptedCredential(_encryptor.Decrypt(ciphertext, additionalAuthenticatedData), false, null);

        var plaintext = DecryptLegacyCbc(ciphertext);
        return new DecryptedCredential(plaintext, true, _encryptor.Encrypt(plaintext, additionalAuthenticatedData));
    }

    /// <summary>Re-encrypts a stored value under the ring's current active key. Legacy CBC values
    /// upgrade to the versioned envelope; envelopes under retired-but-present key ids re-encrypt
    /// under <c>ActiveKeyId</c>. Unknown-key envelopes throw <see cref="PubSoftDecryptionException"/>.</summary>
    public string Rotate(string ciphertext, ReadOnlySpan<byte> additionalAuthenticatedData = default)
    {
        var decrypted = Decrypt(ciphertext, additionalAuthenticatedData);
        return decrypted.ReplacementCiphertext ?? _encryptor.Encrypt(decrypted.Plaintext, additionalAuthenticatedData);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        CryptographicOperations.ZeroMemory(_legacyKey);
        _disposed = true;
    }

    private string DecryptLegacyCbc(string ciphertext)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException)
        {
            throw InvalidLegacyCiphertext();
        }

        if (payload.Length < 32 || (payload.Length - 16) % 16 != 0)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw InvalidLegacyCiphertext();
        }

        var iv = payload.AsSpan(0, 16).ToArray();
        var encrypted = payload.AsSpan(16).ToArray();
        byte[]? plaintext = null;
        try
        {
            using var aes = Aes.Create();
            aes.Key = _legacyKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            plaintext = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return StrictUtf8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is CryptographicException or DecoderFallbackException)
        {
            throw InvalidLegacyCiphertext();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(encrypted);
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static CryptographicException InvalidLegacyCiphertext()
        => new("The legacy credential ciphertext could not be decrypted.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
