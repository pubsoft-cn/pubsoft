using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.Auditing;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using PubSoft.Messaging.Outbox;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class InboxRetentionCleanupTests
{
    [Fact]
    public async Task CleanupDeletesOnlyReceiptsOlderThanRetention()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ReceiptDbContext>().UseSqlite(connection).Options;
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var context = new ReceiptDbContext(options, new FixedTenantContext(1));
        await context.Database.EnsureCreatedAsync();

        var time = new TestTimeProvider(epoch);
        var processor = new EfCoreInboxMessageProcessor<ReceiptDbContext>(context, time);
        Assert.True(await processor.ProcessOnceAsync("consumer", PubSoft.Testing.TestIds.Next(), _ => Task.CompletedTask));
        Assert.True(await processor.ProcessOnceAsync("consumer", PubSoft.Testing.TestIds.Next(), _ => Task.CompletedTask));
        time.SetUtcNow(epoch + TimeSpan.FromDays(2));
        Assert.True(await processor.ProcessOnceAsync("consumer", PubSoft.Testing.TestIds.Next(), _ => Task.CompletedTask));

        var cleanup = new InboxRetentionCleanup<ReceiptDbContext>(context, time);
        var deleted = await cleanup.DeleteProcessedBeforeAsync(TimeSpan.FromDays(1), batchSize: 1);

        Assert.Equal(2, deleted);
        Assert.Single(await context.Set<InboxReceipt>().ToListAsync());
    }

    [Fact]
    public async Task CleanupRestrictsToTenantKeyOrSpansAllTenants()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"cleanup-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ReceiptDbContext>().UseSqlite(connectionString).Options;
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new TestTimeProvider(epoch);

        async Task SeedAsync(long? tenantId)
        {
            await using var context = new ReceiptDbContext(options, new FixedTenantContext(tenantId));
            await context.Database.EnsureCreatedAsync();
            var processor = new EfCoreInboxMessageProcessor<ReceiptDbContext>(context, time);
            Assert.True(await processor.ProcessOnceAsync("consumer", PubSoft.Testing.TestIds.Next(), _ => Task.CompletedTask));
        }

        await SeedAsync(1);
        await SeedAsync(2);
        await SeedAsync(null);

        time.SetUtcNow(epoch + TimeSpan.FromDays(30));

        // Cleanup is a host-scoped maintenance path (spans all rows by design);
        // the write guard requires host scope to delete foreign-tenant rows.
        await using var context = new ReceiptDbContext(options, new FixedTenantContext(null));
        var cleanup = new InboxRetentionCleanup<ReceiptDbContext>(context, time);

        var tenantOneOnly = await cleanup.DeleteProcessedBeforeAsync(TimeSpan.FromDays(1), tenantKey: "1");
        Assert.Equal(1, tenantOneOnly);

        var hostOnly = await cleanup.DeleteProcessedBeforeAsync(TimeSpan.FromDays(1), tenantKey: "host");
        Assert.Equal(1, hostOnly);

        var remaining = await cleanup.DeleteProcessedBeforeAsync(TimeSpan.FromDays(1));
        Assert.Equal(1, remaining);
        Assert.Empty(await context.Set<InboxReceipt>().ToListAsync());
    }

    private sealed class ReceiptDbContext(DbContextOptions<ReceiptDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftOutbox();
        }
    }
}

public sealed class AuditRetentionCleanupTests
{
    [Fact]
    public async Task CleanupDeletesOnlyLogsOlderThanRetention()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var context = new AuditDbContext(options, new FixedTenantContext(1));
        await context.Database.EnsureCreatedAsync();
        context.Set<AuditLogRecord>().AddRange(
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch, Success = true },
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch, Success = true },
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch + TimeSpan.FromDays(2), Success = true });
        await context.SaveChangesAsync();

        var time = new TestTimeProvider(epoch + TimeSpan.FromDays(2));
        var deleted = await new AuditRetentionCleanup<AuditDbContext>(context, time)
            .DeleteStartedBeforeAsync(TimeSpan.FromDays(1), batchSize: 1);

        Assert.Equal(2, deleted);
        Assert.Single(await context.Set<AuditLogRecord>().ToListAsync());
    }

    private sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftAuditPersistence();
        }
    }
}

public sealed class AuditArchiveTests
{
    [Fact]
    public async Task ArchiveExportsBatchesWithEntityChangesBeforeDeleting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var context = new AuditDbContext(options, new FixedTenantContext(1));
        await context.Database.EnsureCreatedAsync();
        var expired = new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch, Success = true };
        expired.EntityChanges.Add(new AuditEntityChangeRecord
        {
            Id = PubSoft.Testing.TestIds.Next(),
            EntityType = "User",
            EntityId = "7",
            Kind = AuditChangeKind.Created,
            ChangedAt = epoch,
            PropertiesJson = "{}",
        });
        context.Set<AuditLogRecord>().AddRange(
            expired,
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch + TimeSpan.FromHours(1), Success = true },
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch + TimeSpan.FromDays(2), Success = true });
        await context.SaveChangesAsync();

        var exported = new List<IReadOnlyList<AuditLogRecord>>();
        var time = new TestTimeProvider(epoch + TimeSpan.FromDays(2));
        var archived = await new AuditRetentionCleanup<AuditDbContext>(context, time)
            .ArchiveStartedBeforeAsync(
                TimeSpan.FromDays(1),
                (batch, _) => { exported.Add(batch); return Task.CompletedTask; },
                batchSize: 1);

        Assert.Equal(2, archived);
        Assert.Equal(2, exported.Count);
        Assert.Single(exported[0]);
        Assert.Single(exported[0][0].EntityChanges);
        Assert.Equal("User", exported[0][0].EntityChanges.First().EntityType);
        Assert.Single(await context.Set<AuditLogRecord>().ToListAsync());
    }

    [Fact]
    public async Task ArchiveStopsWithoutDeletingWhenExportFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var context = new AuditDbContext(options, new FixedTenantContext(1));
        await context.Database.EnsureCreatedAsync();
        context.Set<AuditLogRecord>().AddRange(
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch, Success = true },
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch + TimeSpan.FromHours(1), Success = true });
        await context.SaveChangesAsync();

        var calls = 0;
        var time = new TestTimeProvider(epoch + TimeSpan.FromDays(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AuditRetentionCleanup<AuditDbContext>(context, time)
            .ArchiveStartedBeforeAsync(
                TimeSpan.FromDays(1),
                (_, _) => ++calls == 1 ? Task.CompletedTask : throw new InvalidOperationException("export failed"),
                batchSize: 1));

        // First batch exported + deleted; second batch preserved.
        Assert.Single(await context.Set<AuditLogRecord>().ToListAsync());
    }

    [Fact]
    public async Task ArchiveHonorsTenantRestriction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var context = new AuditDbContext(options, new FixedTenantContext(null));
        await context.Database.EnsureCreatedAsync();
        context.Set<AuditLogRecord>().AddRange(
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch, Success = true, TenantId = 1 },
            new AuditLogRecord { Id = PubSoft.Testing.TestIds.Next(), StartedAt = epoch, Success = true, TenantId = 2 });
        await context.SaveChangesAsync();

        var exported = new List<AuditLogRecord>();
        var time = new TestTimeProvider(epoch + TimeSpan.FromDays(2));
        var archived = await new AuditRetentionCleanup<AuditDbContext>(context, time)
            .ArchiveStartedBeforeAsync(
                TimeSpan.FromDays(1), tenantId: 1,
                (batch, _) => { exported.AddRange(batch); return Task.CompletedTask; });

        Assert.Equal(1, archived);
        Assert.Single(exported);
        Assert.Equal(1, exported[0].TenantId);
        Assert.Single(await context.Set<AuditLogRecord>().ToListAsync());
    }

    private sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftAuditPersistence();
        }
    }
}
