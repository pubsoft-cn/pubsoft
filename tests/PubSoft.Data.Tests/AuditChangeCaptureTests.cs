using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditChangeCaptureTests
{
    [Fact]
    public async Task SensitiveValuesAreRedactedWhenValueCaptureIsEnabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var services = CreateServices(options, captureValues: true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Records.Add(new AuditedRecord { Id = 1, Name = "Ada", PasswordHash = "hash-secret" });
        await context.SaveChangesAsync();

        var collector = scope.ServiceProvider.GetRequiredService<IAuditChangeCollector>();
        var created = Assert.Single(collector.GetSnapshot());
        Assert.Equal(AuditChangeKind.Created, created.Kind);
        Assert.Contains(created.Properties, property => property.Name == nameof(AuditedRecord.Name));
        var password = Assert.Single(created.Properties, property => property.Name == nameof(AuditedRecord.PasswordHash));
        Assert.Equal("[REDACTED]", password.NewValue?.GetString());
        Assert.DoesNotContain("hash-secret", JsonSerializer.Serialize(created));
    }

    [Fact]
    public async Task ValueCaptureDefaultsOffAndCollectorLimitAbortsSave()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var services = CreateServices(options, captureValues: false, maxChanges: 1);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.EnsureCreatedAsync();

        context.Records.Add(new AuditedRecord { Id = 1, Name = "first", PasswordHash = "secret" });
        await context.SaveChangesAsync();
        var change = Assert.Single(scope.ServiceProvider.GetRequiredService<IAuditChangeCollector>().GetSnapshot());
        Assert.Contains(change.Properties, property => property.Name == nameof(AuditedRecord.Name));
        Assert.All(change.Properties, property =>
        {
            Assert.Null(property.OldValue);
            Assert.Null(property.NewValue);
        });

        context.Records.Add(new AuditedRecord { Id = 2, Name = "second", PasswordHash = "secret" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        await using var verify = CreateContext(options);
        Assert.Single(await verify.Records.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CaptureRecordsCreatedUpdatedAndDeletedIntent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var services = CreateServices(options, captureValues: true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.EnsureCreatedAsync();

        var record = new AuditedRecord { Id = 1, Name = "before", PasswordHash = "secret" };
        context.Records.Add(record);
        await context.SaveChangesAsync();
        record.Name = "after";
        await context.SaveChangesAsync();
        context.Records.Remove(record);
        await context.SaveChangesAsync();

        var changes = scope.ServiceProvider.GetRequiredService<IAuditChangeCollector>().GetSnapshot();
        Assert.Collection(changes,
            created => Assert.Equal(AuditChangeKind.Created, created.Kind),
            updated =>
            {
                Assert.Equal(AuditChangeKind.Updated, updated.Kind);
                Assert.Contains(updated.Properties, property => property.Name == nameof(AuditedRecord.Name));
            },
            deleted => Assert.Equal(AuditChangeKind.Deleted, deleted.Kind));
    }

    [Fact]
    public async Task AuditScopeCombinesRequestBusinessAndEntityChangesIntoImmutableSnapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var services = CreateServices(options, captureValues: false);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.EnsureCreatedAsync();

        var auditScope = scope.ServiceProvider.GetRequiredService<IAuditScope>();
        auditScope.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow, "POST", "/api/records", "192.0.2.1"));
        auditScope.SetBusiness(category: "Record", action: "Create", targetId: "1");
        auditScope.SetBusiness(action: "Created", detail: "manual business detail");
        context.Records.Add(new AuditedRecord { Id = 1, Name = "Ada", PasswordHash = "secret" });
        await context.SaveChangesAsync();

        var snapshot = auditScope.Finalize(success: true, statusCode: 201);
        Assert.NotNull(snapshot);
        Assert.Equal("POST", snapshot.Request.HttpMethod);
        Assert.Equal("Record", snapshot.Business.Category);
        Assert.Equal("Created", snapshot.Business.Action);
        Assert.Equal("1", snapshot.Business.TargetId);
        Assert.Equal("manual business detail", snapshot.Business.Detail);
        Assert.Single(snapshot.Changes);
        Assert.Throws<NotSupportedException>(() => ((IList<AuditEntityChange>)snapshot.Changes).Clear());
        Assert.True(auditScope.IsFinalizing);
        Assert.Same(snapshot, auditScope.Finalize(success: true, statusCode: 201));
        Assert.Throws<InvalidOperationException>(() => auditScope.SetBusiness(action: "too late"));
    }

    [Fact]
    public void AuditScopeSkipsEmptySuccessfulRequestsButRetainsFailuresAndChanges()
    {
        var collector = new AuditChangeCollector();
        var scope = new AuditScope(collector, timeProvider: TimeProvider.System);
        scope.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow, "POST", "/api/empty"));
        Assert.Null(scope.Finalize(success: true, statusCode: 200));

        var failed = new AuditScope(collector);
        failed.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow, "GET", "/api/read"));
        failed.MarkFailure("Forbidden");
        var snapshot = failed.Finalize(success: false, statusCode: 403);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Success);
        Assert.Equal("Forbidden", snapshot.FailureCode);

        var getOptions = new AuditScopeOptions { AuditGetRequests = true };
        var auditedGet = new AuditScope(collector, getOptions);
        auditedGet.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow, "GET", "/api/read"));
        Assert.NotNull(auditedGet.Finalize(success: true, statusCode: 200));
    }

    [Fact]
    public void AuditScopeDoesNotExposeExceptionTextAsFailureMetadata()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        scope.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow, "POST", "/api/write"));
        Assert.Throws<ArgumentException>(() => scope.MarkFailure("secret-value\nstack"));
        scope.MarkFailure();

        var snapshot = scope.Finalize(success: false, statusCode: 500);
        Assert.Equal("RequestFailed", snapshot?.FailureCode);
        Assert.DoesNotContain("secret-value", System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task AuditScopeFinalizationIsIdempotentAcrossConcurrentCallers()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        scope.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow, "POST", "/api/write"));
        scope.SetBusiness(action: "Write");

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => scope.Finalize(success: true, statusCode: 200))));

        Assert.All(results, Assert.NotNull);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public async Task EfAuditSinkPersistsTenantScopedSnapshotAndDoesNotRecaptureAuditRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connection).Options;
        var services = CreateServices(options, captureValues: true, tenantId: 42);
        services.AddPubSoftEfCoreAuditPersistence<AuditDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.EnsureCreatedAsync();

        var record = new AuditedRecord { Id = 1, Name = "Ada", PasswordHash = "private-hash" };
        context.Records.Add(record);
        await context.SaveChangesAsync();
        var auditScope = scope.ServiceProvider.GetRequiredService<IAuditScope>();
        auditScope.Initialize(new AuditRequestContext(
            DateTimeOffset.UtcNow,
            "POST",
            "/api/records",
            Actor: new AuditActorContext("actor-42", "Ada", IsAdministrator: true)));
        auditScope.SetBusiness(category: "Record", action: "Create");
        var snapshot = auditScope.Finalize(success: true, statusCode: 201);
        Assert.NotNull(snapshot);

        await scope.ServiceProvider.GetRequiredService<IAuditSnapshotSink>().WriteAsync(snapshot);

        var persisted = await context.AuditLogs.SingleAsync();
        Assert.Equal(42, persisted.TenantId);
        Assert.Equal("actor-42", persisted.SubjectId);
        Assert.Equal("Ada", persisted.SubjectName);
        Assert.True(persisted.IsAdministrator);
        Assert.Equal("Record", persisted.Category);
        var persistedChange = Assert.Single(await context.AuditEntityChanges.ToListAsync());
        Assert.Equal(persisted.Id, persistedChange.AuditLogId);
        Assert.Contains("[REDACTED]", persistedChange.PropertiesJson);
        Assert.DoesNotContain("private-hash", persistedChange.PropertiesJson);
        Assert.Single(scope.ServiceProvider.GetRequiredService<IAuditChangeCollector>().GetSnapshot());

        await using var otherTenant = new AuditDbContext(options, new TenantContext(43));
        Assert.Empty(await otherTenant.AuditLogs.ToListAsync());
    }

    [Fact]
    public void AuditOptionsValidateMemoryAndStringLimits()
    {
        Assert.False(new AuditChangeCollectorOptions().CaptureValues);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuditChangeCollector(new AuditChangeCollectorOptions { MaxChanges = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuditChangeCollector(new AuditChangeCollectorOptions { MaxChangedPropertiesPerEntity = 1_001 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuditChangeCollector(new AuditChangeCollectorOptions { MaxStringValueLength = 65_537 }));

        var serialized = AuditValueSerializer.Serialize(new string('x', 8), maxStringLength: 4);
        Assert.Equal("xxxx...", serialized?.GetString());
    }

    private static ServiceCollection CreateServices(
        DbContextOptions<AuditDbContext> options,
        bool captureValues,
        int maxChanges = 100,
        long? tenantId = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddScoped<ITenantContext>(_ => new TenantContext(tenantId));
        services.AddScoped<AuditDbContext>();
        services.AddPubSoftAuditChangeCapture(settings =>
        {
            settings.CaptureValues = captureValues;
            settings.MaxChanges = maxChanges;
        });
        return services;
    }

    private static AuditDbContext CreateContext(DbContextOptions<AuditDbContext> options)
        => new(options, new TenantContext());

    private sealed class AuditDbContext(
        DbContextOptions<AuditDbContext> options,
        ITenantContext tenantContext,
        ISaveChangesHook? saveChangesHook = null)
        : PubSoftDbContext(options, tenantContext, saveChangesHook: saveChangesHook)
    {
        public DbSet<AuditedRecord> Records => Set<AuditedRecord>();
        public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();
        public DbSet<AuditEntityChangeRecord> AuditEntityChanges => Set<AuditEntityChangeRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftAuditPersistence();
        }
    }

    private sealed class AuditedRecord : IMultiTenant, ISoftDelete
    {
        public int Id { get; set; }
        public long? TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public long? DeleterId { get; set; }
        public DateTime? DeletionTime { get; set; }
    }

    private sealed class TenantContext(long? tenantId = null) : ITenantContext
    {
        public long? TenantId => tenantId;
    }
}