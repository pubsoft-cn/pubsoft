using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Cryptography;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EncryptionConverterWaveTests : IDisposable
{
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }

    private sealed class SecretHolder
    {
        public long Id { get; set; }
        public string? Secret { get; set; }
    }

    private sealed class EncCtx : PubSoftDbContext
    {
        private readonly CredentialEncryptionService _encryption;

        public EncCtx(DbContextOptions<EncCtx> o, CredentialEncryptionService encryption)
            : base(o, new Tenant()) => _encryption = encryption;

        public DbSet<SecretHolder> Holders => Set<SecretHolder>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<SecretHolder>().Property(h => h.Secret)
                .HasPubSoftEncryption(_encryption, "SecretHolder.Secret");
        }
    }

    private sealed class FixedKeyRing : IEncryptionKeyRing
    {
        public string ActiveKeyId => "k1";
        public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
        {
            key = keyId == "k1" ? new byte[32] : ReadOnlyMemory<byte>.Empty;
            return keyId == "k1";
        }
    }

    private readonly CredentialEncryptionService _encryption =
        new(new AesGcmEncryptor(new FixedKeyRing()), "legacy-key-material");

    private readonly Microsoft.Data.Sqlite.SqliteConnection _conn = new("Data Source=:memory:");

    private EncCtx NewCtx()
    {
        var ctx = new EncCtx(
            new DbContextOptionsBuilder<EncCtx>().UseSqlite(_conn).Options, _encryption);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task SecretStoredAsCiphertextAndReadsBackPlaintext()
    {
        _conn.Open();
        await using (var ctx = NewCtx())
        {
            ctx.Holders.Add(new SecretHolder { Id = 1, Secret = "plain-secret-value" });
            await ctx.SaveChangesAsync();
        }

        string raw;
        await using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Secret FROM Holders WHERE Id = 1";
            raw = (string)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.StartsWith("pubsoft:", raw);
        Assert.DoesNotContain("plain-secret-value", raw);

        await using (var ctx = NewCtx())
        {
            var holder = await ctx.Holders.FindAsync(1L);
            Assert.Equal("plain-secret-value", holder!.Secret);
        }
    }

    public void Dispose()
    {
        _encryption.Dispose();
        _conn.Dispose();
    }
}
