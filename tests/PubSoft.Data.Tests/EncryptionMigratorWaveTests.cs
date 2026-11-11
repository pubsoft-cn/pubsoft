using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.Cryptography;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

/// <summary>Coverage for PubSoftEncryptionMigrator classification, CAS and batch guards.</summary>
public sealed class EncryptionMigratorWaveTests
{
    private sealed class Secret { public long Id { get; set; } public string? Value { get; set; } }
    private sealed class Ctx(DbContextOptions<Ctx> o, CredentialEncryptionService enc) : DbContext(o)
    {
        private readonly CredentialEncryptionService _enc = enc;
        public DbSet<Secret> Secrets => Set<Secret>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Secret>().Property(x => x.Value).HasPubSoftEncryption(_enc, "secret:value");
        }
    }

    private static CredentialEncryptionService Service(byte[]? key = null)
        => new(new AesGcmEncryptor(new Ring("k1", key ?? RandomNumberGenerator.GetBytes(32))), "legacy");

    private sealed class Ring(string id, byte[] key) : IEncryptionKeyRing
    {
        private readonly byte[] _key = key;
        public string ActiveKeyId { get; } = id;
        public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
        { key = _key; return keyId == id; }
    }

    private static async Task<(Ctx, SqliteConnection, CredentialEncryptionService)> Open()
    {
        var enc = Service();
        var connStr = $"Data Source=mig{System.Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        // anchor keeps the shared-cache in-memory DB alive for the whole test;
        // EF opens/closes its own connection freely.
        var anchor = new SqliteConnection(connStr);
        await anchor.OpenAsync();
        var ctx = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite(connStr).Options, enc);
        await ctx.Database.EnsureCreatedAsync();
        return (ctx, anchor, enc);
    }

    private static async Task Seed(Ctx ctx, long id, string value)
        => await ctx.Database.ExecuteSqlAsync($"INSERT INTO Secrets (Id, Value) VALUES ({id}, {value})");

    private static async Task<string?> Read(Ctx ctx, long id)
    {
        var conn = ctx.Database.GetDbConnection();  // shared context connection - do not dispose
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Secrets WHERE Id = " + id;
        return (string?)await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task UndecryptableValueIsSkippedByDefault()
    {
        var (ctx, anchor, enc) = await Open();
        await Seed(ctx, 1, "not-decryptable-possibly-legacy");   // ambiguous: plaintext or bad-key ciphertext
        var report = await new PubSoftEncryptionMigrator(enc).MigrateAsync(ctx);
        Assert.Equal(0, report.Rewritten);
        Assert.Equal(1, report.SkippedAmbiguous);
        Assert.Equal("not-decryptable-possibly-legacy", await Read(ctx, 1)); // original untouched
    }

    [Fact]
    public async Task OptInRewritesPlaintext()
    {
        var (ctx, anchor, enc) = await Open();
        await Seed(ctx, 1, "real-plaintext");
        var report = await new PubSoftEncryptionMigrator(enc).MigrateAsync(ctx, treatUndecryptableAsPlaintext: true);
        Assert.Equal(1, report.Rewritten);
        Assert.StartsWith("pubsoft:", await Read(ctx, 1));
        Assert.Equal("real-plaintext", enc.Decrypt(await Read(ctx, 1)!, System.Text.Encoding.UTF8.GetBytes("secret:value")).Plaintext);
    }

    [Fact]
    public async Task LegacyCbcIsRewrittenToEnvelope()
    {
        var (ctx, anchor, enc) = await Open();
        var legacyB64 = LegacyCbc("legacy", "legacy");
        await Seed(ctx, 1, legacyB64);
        var report = await new PubSoftEncryptionMigrator(enc).MigrateAsync(ctx);
        Assert.Equal(1, report.Rewritten);
        Assert.StartsWith("pubsoft:", await Read(ctx, 1));
    }

    [Fact]
    public async Task AlreadyEnvelopedRowsAreIgnored()
    {
        var (ctx, anchor, enc) = await Open();
        await Seed(ctx, 1, enc.Encrypt("done", System.Text.Encoding.UTF8.GetBytes("secret:value")));
        var report = await new PubSoftEncryptionMigrator(enc).MigrateAsync(ctx);
        Assert.Equal(0, report.Rewritten);
        Assert.Equal(0, report.SkippedAmbiguous);
    }

    private static string LegacyCbc(string plaintext, string keyMaterial)
    {
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(keyMaterial));
        var iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        var ct = aes.CreateEncryptor().TransformFinalBlock(
            System.Text.Encoding.UTF8.GetBytes(plaintext), 0, plaintext.Length);
        return Convert.ToBase64String([.. iv, .. ct]);
    }

    [Fact]
    public async Task ZeroBatchSizeThrows()
    {
        var (ctx, anchor, enc) = await Open();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new PubSoftEncryptionMigrator(enc).MigrateAsync(ctx, batchSize: 0));
    }
}
