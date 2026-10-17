using System.Buffers.Binary;
using System.Text;

namespace PubSoft.Cryptography;

/// <summary>
/// SM3 密码杂凑算法（GB/T 32905-2016）+ HMAC-SM3 —— 纯托管实现，零外部依赖。
/// <para>用于国密互通场景（银联/政务/渠道签名）。输出固定 256-bit。</para>
/// </summary>
public static class Sm3
{
    /// <summary>SM3 摘要长度（字节）。</summary>
    public const int DigestSize = 32;

    private const int BlockSize = 64;

    // 初始向量 IV（GB/T 32905）
    private static readonly uint[] Iv =
    [
        0x7380166F, 0x4914B2B9, 0x172442D7, 0xDA8A0600,
        0xA96F30BC, 0x163138AA, 0xE38DEE4D, 0xB0FB0E4E,
    ];

    private static uint T(int j) => j < 16 ? 0x79CC4519u : 0x7A879D8Au;

    private static uint FF(uint x, uint y, uint z, int j)
        => j < 16 ? x ^ y ^ z : (x & y) | (x & z) | (y & z);

    private static uint GG(uint x, uint y, uint z, int j)
        => j < 16 ? x ^ y ^ z : (x & y) | (~x & z);

    private static uint P0(uint x) => x ^ Rotl(x, 9) ^ Rotl(x, 17);

    private static uint P1(uint x) => x ^ Rotl(x, 15) ^ Rotl(x, 23);

    private static uint Rotl(uint x, int n) => (x << n) | (x >> (32 - n));

    /// <summary>计算 SM3 摘要，返回 32 字节。</summary>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var state = (uint[])Iv.Clone();
        HashCore(state, data);
        var output = new byte[DigestSize];
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(i * 4), state[i]);
        return output;
    }

    /// <summary>计算 SM3 摘要（UTF-8 文本），返回小写 hex。</summary>
    public static string HashHex(string data) => ToLowerHex(Hash(Encoding.UTF8.GetBytes(data)));

    /// <summary>计算 HMAC-SM3（HMAC 结构套 SM3，块长 64 字节）。</summary>
    public static byte[] Hmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        // HMAC: H((K' ^ opad) || H((K' ^ ipad) || data)),  K' = key 超块长则先 H(key)，后补零至块长
        Span<byte> k = stackalloc byte[BlockSize];
        k.Clear();
        if (key.Length > BlockSize)
            Hash(key).CopyTo(k);
        else
            key.CopyTo(k);

        Span<byte> innerPad = stackalloc byte[BlockSize];
        Span<byte> outerPad = stackalloc byte[BlockSize];
        for (var i = 0; i < BlockSize; i++)
        {
            innerPad[i] = (byte)(k[i] ^ 0x36);
            outerPad[i] = (byte)(k[i] ^ 0x5C);
        }

        var innerState = (uint[])Iv.Clone();
        HashCore(innerState, innerPad, data);
        var inner = new byte[DigestSize];
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(inner.AsSpan(i * 4), innerState[i]);

        var outerState = (uint[])Iv.Clone();
        HashCore(outerState, outerPad, inner);
        var result = new byte[DigestSize];
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4), outerState[i]);
        return result;
    }

    /// <summary>计算 HMAC-SM3（UTF-8 文本），返回 Base64。</summary>
    public static string HmacBase64(string key, string data)
        => Convert.ToBase64String(Hmac(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data)));

    /// <summary>计算 HMAC-SM3，返回小写 hex。</summary>
    public static string HmacHex(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
        => ToLowerHex(Hmac(key, data));

    /// <summary>Merkle–Damgård 主循环：填充 → 分块压缩，就地更新 state。</summary>
    private static void HashCore(uint[] state, ReadOnlySpan<byte> data)
        => HashCore(state, data, ReadOnlySpan<byte>.Empty);

    private static void HashCore(uint[] state, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var totalLen = (long)first.Length + second.Length;
        // bitLen = totalLen * 8，填充格式：data || 0x80 || 0x00* || len64be
        var padded = totalLen + 1 + 8;
        var blocks = (int)((padded + BlockSize - 1) / BlockSize);
        var msgLen = blocks * BlockSize;

        Span<byte> block = stackalloc byte[BlockSize];
        Span<uint> w = stackalloc uint[68];
        Span<uint> w1 = stackalloc uint[64];

        long offset = 0;
        for (var b = 0; b < blocks; b++)
        {
            block.Clear();
            for (var i = 0; i < BlockSize; i++)
            {
                var pos = offset + i;
                if (pos < first.Length)
                    block[i] = first[(int)pos];
                else if (pos < totalLen)
                    block[i] = second[(int)(pos - first.Length)];
                else if (pos == totalLen)
                    block[i] = 0x80;
            }
            if (b == blocks - 1)
                BinaryPrimitives.WriteInt64BigEndian(block[(BlockSize - 8)..], totalLen * 8);

            Compress(state, block, w, w1);
            offset += BlockSize;
        }
    }

    private static void Compress(uint[] state, ReadOnlySpan<byte> block, Span<uint> w, Span<uint> w1)
    {
        for (var i = 0; i < 16; i++)
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(i * 4, 4));
        for (var j = 16; j < 68; j++)
            w[j] = P1(w[j - 16] ^ w[j - 9] ^ Rotl(w[j - 3], 15)) ^ Rotl(w[j - 13], 7) ^ w[j - 6];
        for (var j = 0; j < 64; j++)
            w1[j] = w[j] ^ w[j + 4];

        var a = state[0]; var b2 = state[1]; var c = state[2]; var d = state[3];
        var e = state[4]; var f = state[5]; var g = state[6]; var h = state[7];

        for (var j = 0; j < 64; j++)
        {
            var ss1 = Rotl(Rotl(a, 12) + e + Rotl(T(j), j % 32), 7);
            var ss2 = ss1 ^ Rotl(a, 12);
            var tt1 = FF(a, b2, c, j) + d + ss2 + w1[j];
            var tt2 = GG(e, f, g, j) + h + ss1 + w[j];
            d = c; c = Rotl(b2, 9); b2 = a; a = tt1;
            h = g; g = Rotl(f, 19); f = e; e = P0(tt2);
        }

        state[0] ^= a; state[1] ^= b2; state[2] ^= c; state[3] ^= d;
        state[4] ^= e; state[5] ^= f; state[6] ^= g; state[7] ^= h;
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length * 2];
        const string hex = "0123456789abcdef";
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = hex[bytes[i] >> 4];
            chars[i * 2 + 1] = hex[bytes[i] & 0xF];
        }
        return new string(chars);
    }
}
