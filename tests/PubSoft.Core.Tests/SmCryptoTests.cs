using System.Text;
using PubSoft.Cryptography;
using Xunit;

namespace PubSoft.Core.Tests;

public class SmCryptoTests
{
    // GB/T 32905 标准向量
    [Fact]
    public void Sm3_Hash_GbVector_Abc()
        => Assert.Equal("66c7f0f462eeedd9d1f2d46bdc10e4e24167c4875cf2f7a2297da02b8f4ba8e0",
            Sm3.HashHex("abc"));

    [Fact]
    public void Sm3_Hash_GbVector_64Bytes()
    {
        var data = string.Concat(Enumerable.Repeat("abcd", 16));
        Assert.Equal("debe9ff92275b8a138604889c18e5a4d6fdb70e5387e5765293dcba39c0c5732",
            Sm3.HashHex(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(128)]
    [InlineData(200)]
    public void Sm3_Matches_BouncyCastle(int length)
    {
        var data = Enumerable.Range(0, length).Select(i => (byte)(i * 31 + 7)).ToArray();
        var expected = new Org.BouncyCastle.Crypto.Digests.SM3Digest();
        var buf = new byte[expected.GetDigestSize()];
        expected.BlockUpdate(data, 0, data.Length);
        expected.DoFinal(buf, 0);
        Assert.Equal(buf, Sm3.Hash(data));
    }

    [Fact]
    public void Sm3_Hmac_Matches_BouncyCastle()
    {
        var key = Encoding.UTF8.GetBytes("secret-key-0123456789");
        var data = Encoding.UTF8.GetBytes("payload to authenticate");
        var bc = new Org.BouncyCastle.Crypto.Macs.HMac(new Org.BouncyCastle.Crypto.Digests.SM3Digest());
        bc.Init(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key));
        var expected = new byte[bc.GetMacSize()];
        bc.BlockUpdate(data, 0, data.Length);
        bc.DoFinal(expected, 0);
        Assert.Equal(expected, Sm3.Hmac(key, data));
    }

    [Fact]
    public void Sm3_Hmac_LongKey_Matches_BouncyCastle()
    {
        // key 超块长（64B）时先 H(key) 的 K' 归一化路径
        var key = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var data = Encoding.UTF8.GetBytes("x");
        var bc = new Org.BouncyCastle.Crypto.Macs.HMac(new Org.BouncyCastle.Crypto.Digests.SM3Digest());
        bc.Init(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key));
        var expected = new byte[bc.GetMacSize()];
        bc.BlockUpdate(data, 0, data.Length);
        bc.DoFinal(expected, 0);
        Assert.Equal(expected, Sm3.Hmac(key, data));
    }

    // GB/T 32907 标准向量：单分组
    [Fact]
    public void Sm4_GbVector_SingleBlock()
    {
        var key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
        var plain = Convert.FromHexString("0123456789abcdeffedcba9876543210");
        var cipher = Sm4.EncryptEcb(key, plain);
        // 单分组明文经 PKCS7 填充后得两个分组，第一分组必须命中标准向量
        Assert.Equal("681edf34d206965e86b3e94f536e4246", Convert.ToHexString(cipher[..16]).ToLowerInvariant());
    }

    [Fact]
    public void Sm4_RoundTrip()
    {
        var key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
        var plain = Encoding.UTF8.GetBytes("国密互通明文 some utf8 text");
        var cipher = Sm4.EncryptEcb(key, plain);
        Assert.Equal(plain, Sm4.DecryptEcb(key, cipher));
    }

    [Fact]
    public void Sm4_Block_Aligned_RoundTrip()
    {
        var key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
        var plain = new byte[32]; // 块对齐长度也要正确加/去填充
        Array.Fill(plain, (byte)0xAB);
        var cipher = Sm4.EncryptEcb(key, plain);
        Assert.Equal(48, cipher.Length);
        Assert.Equal(plain, Sm4.DecryptEcb(key, cipher));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(64)]
    public void Sm4_Matches_BouncyCastle(int length)
    {
        var key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
        var data = Enumerable.Range(0, length).Select(i => (byte)(i * 17 + 3)).ToArray();
        var bc = new Org.BouncyCastle.Crypto.Engines.SM4Engine();
        bc.Init(true, new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key));

        var ours = Sm4.EncryptEcb(key, data);
        // BouncyCastle 裸引擎不带填充：对我方输出逐块比对前 floor(len/16) 块 + 填充块另行验证
        var bcOut = new byte[ours.Length];
        var padded = new byte[ours.Length];
        Array.Copy(data, padded, data.Length);
        var pad = ours.Length - data.Length;
        for (var i = data.Length; i < padded.Length; i++)
            padded[i] = (byte)pad;
        for (var i = 0; i < padded.Length; i += 16)
            bc.ProcessBlock(padded, i, bcOut, i);
        Assert.Equal(bcOut, ours);
    }

    [Fact]
    public void Sm4_Invalid_Padding_Throws()
    {
        var key = Convert.FromHexString("0123456789abcdeffedcba9876543210");
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => Sm4.DecryptEcb(key, new byte[16]));
    }

    [Fact]
    public void Sm4_Rejects_Short_Ciphertext()
        => Assert.Throws<ArgumentException>(() => Sm4.DecryptEcb(new byte[16], new byte[8]));

    [Fact]
    public void Sm4_Rejects_Bad_Key_Length()
        => Assert.Throws<ArgumentException>(() => Sm4.EncryptEcb(new byte[8], "x"u8.ToArray()));
}
