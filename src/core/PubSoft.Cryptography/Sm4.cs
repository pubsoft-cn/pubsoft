using System.Buffers.Binary;
using System.Text;

namespace PubSoft.Cryptography;

/// <summary>
/// SM4 分组密码（GB/T 32907-2016）—— 纯托管实现，零外部依赖。
/// <para>提供 ECB + PKCS7 便捷封装，用于银联/政务等存量国密互通场景。</para>
/// <para><b>边界</b>：ECB 模式泄露明文分组模式，仅限协议兼容；新设计的机密存储请用 <see cref="AesEncryptor"/>（AES-GCM）。</para>
/// </summary>
public static class Sm4
{
    /// <summary>SM4 分组长（字节）。</summary>
    public const int BlockSize = 16;

    /// <summary>SM4 密钥长（字节）。</summary>
    public const int KeySize = 16;

    private static readonly byte[] SBox =
    [
        0xD6,0x90,0xE9,0xFE,0xCC,0xE1,0x3D,0xB7,0x16,0xB6,0x14,0xC2,0x28,0xFB,0x2C,0x05,
        0x2B,0x67,0x9A,0x76,0x2A,0xBE,0x04,0xC3,0xAA,0x44,0x13,0x26,0x49,0x86,0x06,0x99,
        0x9C,0x42,0x50,0xF4,0x91,0xEF,0x98,0x7A,0x33,0x54,0x0B,0x43,0xED,0xCF,0xAC,0x62,
        0xE4,0xB3,0x1C,0xA9,0xC9,0x08,0xE8,0x95,0x80,0xDF,0x94,0xFA,0x75,0x8F,0x3F,0xA6,
        0x47,0x07,0xA7,0xFC,0xF3,0x73,0x17,0xBA,0x83,0x59,0x3C,0x19,0xE6,0x85,0x4F,0xA8,
        0x68,0x6B,0x81,0xB2,0x71,0x64,0xDA,0x8B,0xF8,0xEB,0x0F,0x4B,0x70,0x56,0x9D,0x35,
        0x1E,0x24,0x0E,0x5E,0x63,0x58,0xD1,0xA2,0x25,0x22,0x7C,0x3B,0x01,0x21,0x78,0x87,
        0xD4,0x00,0x46,0x57,0x9F,0xD3,0x27,0x52,0x4C,0x36,0x02,0xE7,0xA0,0xC4,0xC8,0x9E,
        0xEA,0xBF,0x8A,0xD2,0x40,0xC7,0x38,0xB5,0xA3,0xF7,0xF2,0xCE,0xF9,0x61,0x15,0xA1,
        0xE0,0xAE,0x5D,0xA4,0x9B,0x34,0x1A,0x55,0xAD,0x93,0x32,0x30,0xF5,0x8C,0xB1,0xE3,
        0x1D,0xF6,0xE2,0x2E,0x82,0x66,0xCA,0x60,0xC0,0x29,0x23,0xAB,0x0D,0x53,0x4E,0x6F,
        0xD5,0xDB,0x37,0x45,0xDE,0xFD,0x8E,0x2F,0x03,0xFF,0x6A,0x72,0x6D,0x6C,0x5B,0x51,
        0x8D,0x1B,0xAF,0x92,0xBB,0xDD,0xBC,0x7F,0x11,0xD9,0x5C,0x41,0x1F,0x10,0x5A,0xD8,
        0x0A,0xC1,0x31,0x88,0xA5,0xCD,0x7B,0xBD,0x2D,0x74,0xD0,0x12,0xB8,0xE5,0xB4,0xB0,
        0x89,0x69,0x97,0x4A,0x0C,0x96,0x77,0x7E,0x65,0xB9,0xF1,0x09,0xC5,0x6E,0xC6,0x84,
        0x18,0xF0,0x7D,0xEC,0x3A,0xDC,0x4D,0x20,0x79,0xEE,0x5F,0x3E,0xD7,0xCB,0x39,0x48,
    ];

    private static readonly uint[] Fk = [0xA3B1BAC6, 0x56AA3350, 0x677D9197, 0xB27022DC];

    private static readonly uint[] Ck =
    [
        0x00070E15,0x1C232A31,0x383F464D,0x545B6269,
        0x70777E85,0x8C939AA1,0xA8AFB6BD,0xC4CBD2D9,
        0xE0E7EEF5,0xFC030A11,0x181F262D,0x343B4249,
        0x50575E65,0x6C737A81,0x888F969D,0xA4ABB2B9,
        0xC0C7CED5,0xDCE3EAF1,0xF8FF060D,0x141B2229,
        0x30373E45,0x4C535A61,0x686F767D,0x848B9299,
        0xA0A7AEB5,0xBCC3CAD1,0xD8DFE6ED,0xF4FB0209,
        0x10171E25,0x2C333A41,0x484F565D,0x646B7279,
    ];

    private static uint Rotl(uint x, int n) => (x << n) | (x >> (32 - n));

    private static uint Tau(uint a)
    {
        var b = (uint)((SBox[a >> 24] << 24) | (SBox[(a >> 16) & 0xFF] << 16)
            | (SBox[(a >> 8) & 0xFF] << 8) | SBox[a & 0xFF]);
        return b;
    }

    // 加密轮函数线性变换 L(B) = B ^ (B<<<2) ^ (B<<<10) ^ (B<<<18) ^ (B<<<24)
    private static uint L(uint b) => b ^ Rotl(b, 2) ^ Rotl(b, 10) ^ Rotl(b, 18) ^ Rotl(b, 24);

    // 密钥扩展线性变换 L'(B) = B ^ (B<<<13) ^ (B<<<23)
    private static uint LPrime(uint b) => b ^ Rotl(b, 13) ^ Rotl(b, 23);

    private static uint T(uint x) => L(Tau(x));

    private static uint TPrime(uint x) => LPrime(Tau(x));

    /// <summary>展开 32 轮密钥。decrypt 时逆序使用。</summary>
    private static uint[] ExpandKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"SM4 key must be {KeySize} bytes.", nameof(key));

        Span<uint> mk = stackalloc uint[4];
        for (var i = 0; i < 4; i++)
            mk[i] = BinaryPrimitives.ReadUInt32BigEndian(key.Slice(i * 4, 4));

        Span<uint> k = stackalloc uint[36];
        for (var i = 0; i < 4; i++)
            k[i] = mk[i] ^ Fk[i];

        var rk = new uint[32];
        for (var i = 0; i < 32; i++)
        {
            k[i + 4] = k[i] ^ TPrime(k[i + 1] ^ k[i + 2] ^ k[i + 3] ^ Ck[i]);
            rk[i] = k[i + 4];
        }
        return rk;
    }

    /// <summary>对单个 16 字节分组做 SM4 变换（原地输入输出均为大端块）。</summary>
    private static void CryptBlock(ReadOnlySpan<byte> input, Span<byte> output, uint[] rk, bool decrypt)
    {
        Span<uint> x = stackalloc uint[36];
        for (var i = 0; i < 4; i++)
            x[i] = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(i * 4, 4));

        for (var i = 0; i < 32; i++)
        {
            var roundKey = decrypt ? rk[31 - i] : rk[i];
            x[i + 4] = x[i] ^ T(x[i + 1] ^ x[i + 2] ^ x[i + 3] ^ roundKey);
        }

        // 反序输出 R(X32..X35)
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt32BigEndian(output.Slice(i * 4, 4), x[35 - i]);
    }

    /// <summary>SM4-ECB 加密（自动 PKCS7 填充）。key/明文 → 密文。</summary>
    public static byte[] EncryptEcb(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext)
    {
        var rk = ExpandKey(key);
        var paddedLen = (plaintext.Length / BlockSize + 1) * BlockSize;
        var padded = new byte[paddedLen];
        plaintext.CopyTo(padded);
        var pad = (byte)(paddedLen - plaintext.Length);
        for (var i = plaintext.Length; i < paddedLen; i++)
            padded[i] = pad;

        var output = new byte[paddedLen];
        for (var i = 0; i < paddedLen; i += BlockSize)
            CryptBlock(padded.AsSpan(i, BlockSize), output.AsSpan(i, BlockSize), rk, decrypt: false);
        return output;
    }

    /// <summary>SM4-ECB 解密并去 PKCS7 填充。填充非法时抛 <see cref="System.Security.Cryptography.CryptographicException"/>。</summary>
    public static byte[] DecryptEcb(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.Length == 0 || ciphertext.Length % BlockSize != 0)
            throw new ArgumentException("SM4-ECB ciphertext length must be a non-empty multiple of 16.", nameof(ciphertext));

        var rk = ExpandKey(key);
        var output = new byte[ciphertext.Length];
        for (var i = 0; i < ciphertext.Length; i += BlockSize)
            CryptBlock(ciphertext.Slice(i, BlockSize), output.AsSpan(i, BlockSize), rk, decrypt: true);

        var pad = output[^1];
        if (pad == 0 || pad > BlockSize)
            throw new System.Security.Cryptography.CryptographicException("Invalid SM4 padding.");
        // 校验全部填充字节，防 padding-oracle 形变
        for (var i = output.Length - pad; i < output.Length; i++)
            if (output[i] != pad)
                throw new System.Security.Cryptography.CryptographicException("Invalid SM4 padding.");
        return output[..^pad];
    }

    /// <summary>SM4-ECB 加密：hex 密钥 + UTF-8 明文 → 小写 hex 密文。</summary>
    public static string EncryptEcbHex(string hexKey, string plaintext)
    {
        var key = Convert.FromHexString(hexKey);
        var cipher = EncryptEcb(key, Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(cipher).ToLowerInvariant();
    }

    /// <summary>SM4-ECB 解密：hex 密钥 + hex 密文 → UTF-8 明文。</summary>
    public static string DecryptEcbHex(string hexKey, string cipherHex)
    {
        var key = Convert.FromHexString(hexKey);
        var plain = DecryptEcb(key, Convert.FromHexString(cipherHex));
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>SM4-ECB 加密：Base64 密钥 + UTF-8 明文 → 小写 hex 密文。</summary>
    public static string EncryptEcbBase64Key(string base64Key, string plaintext)
        => EncryptEcbHex(Convert.ToHexString(Convert.FromBase64String(base64Key)), plaintext);
}
