using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Cryptography;

/// <summary>
/// AES-256-GCM encryptor for at-rest sensitive data (e.g. client JWT signing keys).
/// <para>The master key is injected from environment variables / a secrets manager and never leaves the service process.</para>
/// <para>Format: base64(iv) + ":" + base64(ciphertext+tag)</para>
/// </summary>
public static class AesEncryptor
{

    /// <summary>Encrypts plaintext, returns base64(iv):base64(ciphertext+tag).</summary>
    public static string Encrypt(string plaintext, string masterKey)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        if (string.IsNullOrEmpty(masterKey))
            throw new InvalidOperationException("加密主密钥未配置");

        var key = DeriveKey(masterKey);
        var iv = RandomNumberGenerator.GetBytes(12); // GCM 推荐 12 字节 nonce
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16]; // GCM tag

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(iv, plaintextBytes, ciphertext, tag);

        // iv(12) + tag(16) + ciphertext
        var combined = new byte[iv.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
        Buffer.BlockCopy(tag, 0, combined, iv.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, iv.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    /// <summary>Decrypts base64(iv):base64(ciphertext+tag), returns plaintext.</summary>
    public static string Decrypt(string encrypted, string masterKey)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        if (string.IsNullOrEmpty(masterKey))
            throw new InvalidOperationException("加密主密钥未配置");

        var key = DeriveKey(masterKey);
        var combined = Convert.FromBase64String(encrypted);

        var iv = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[combined.Length - 12 - 16];

        Buffer.BlockCopy(combined, 0, iv, 0, 12);
        Buffer.BlockCopy(combined, 12, tag, 0, 16);
        Buffer.BlockCopy(combined, 28, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(iv, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>Generates a 256-bit random key (base64, 44 chars).</summary>
    public static string GenerateSecretKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }
    private static byte[] DeriveKey(string masterKey)
    {
        // 用 SHA-256 派生 32 字节密钥（AES-256）
        return SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
    }
}
