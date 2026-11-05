using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PubSoft.Authorization;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class PermissionGrantPersistenceTests
{
    [Fact]
    public async Task EfProviderUnionsEnabledRoleGrantsAndExcludesDisabledOrForeignTenantRoles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseSqlite(connection).Options;
        var catalog = new PermissionDefinitionRegistry(
        [
            new TestPermissionDefinitionProvider()
        ]);

        await using (var seed = CreateContext(options, 7))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Roles.AddRange(
                new PermissionRole(1, "reader"),
                new PermissionRole(2, "editor"),
                new PermissionRole(3, "disabled") { IsEnabled = false });
            seed.SubjectRoles.AddRange(
                new SubjectPermissionRole(11, 42, 1),
                new SubjectPermissionRole(12, 42, 2),
                new SubjectPermissionRole(13, 42, 3));
            seed.Grants.AddRange(
                new PermissionGrantRecord(21, 1, "records", flags: (byte)PermissionFlags.Query),
                new PermissionGrantRecord(22, 2, "records", flags: (byte)PermissionFlags.Modify),
                new PermissionGrantRecord(23, 3, "records", flags: (byte)PermissionFlags.Delete));
            await seed.SaveChangesAsync();
        }

        // Foreign-tenant fixture rows go in under that tenant's own scope — the
        // write guard now rejects explicit foreign TenantIds on Added entities.
        await using (var foreign = CreateContext(options, 8))
        {
            foreign.Roles.Add(new PermissionRole(4, "other-tenant", tenantId: 8));
            foreign.SubjectRoles.Add(new SubjectPermissionRole(14, 42, 4, tenantId: 8));
            foreign.Grants.Add(new PermissionGrantRecord(24, 4, "records", tenantId: 8, flags: (byte)PermissionFlags.Delete));
            await foreign.SaveChangesAsync();
        }

        await using var context = CreateContext(options, 7);
        var provider = new EfCorePermissionGrantProvider<PermissionDbContext>(context);
        var grants = await provider.GetGrantsAsync(42, 7);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.Equal(2, grants.Count);
        Assert.True(await service.IsGrantedAsync(42, 7, "records", PermissionFlags.Query));
        Assert.True(await service.IsGrantedAsync(42, 7, "records", PermissionFlags.Modify));
        Assert.True(await service.IsGrantedAsync(42, 7, "records", PermissionFlags.Query | PermissionFlags.Modify));
        Assert.False(await service.IsGrantedAsync(42, 7, "records", PermissionFlags.Delete));
        Assert.False(await service.IsGrantedAsync(42, 8, "records", PermissionFlags.Query));
    }

    [Fact]
    public async Task EfProviderRejectsResolutionOutsideItsTenantScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseSqlite(connection).Options;
        await using var context = CreateContext(options, 7);
        await context.Database.EnsureCreatedAsync();

        var provider = new EfCorePermissionGrantProvider<PermissionDbContext>(context);

        Assert.Empty(await provider.GetGrantsAsync(42, 8));
    }

    [Fact]
    public async Task GrantManagerReplacesRoleGrantsAndAssignmentsForEvaluator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseSqlite(connection).Options;
        await using var context = CreateContext(options, 7);
        await context.Database.EnsureCreatedAsync();
        context.Roles.Add(new PermissionRole(51, "operator"));
        await context.SaveChangesAsync();

        var manager = new EfCorePermissionGrantManager<PermissionDbContext>(context);
        await manager.AssignSubjectRolesAsync(200, 7, [51]);
        await manager.ReplaceRoleGrantsAsync(51, 7,
        [
            new PermissionGrantRecord(61, 51, "records", tenantId: 7,
                flags: (byte)(PermissionFlags.Query | PermissionFlags.Modify)),
            new PermissionGrantRecord(62, 51, "records", tenantId: 7, customPermissionName: "export"),
            new PermissionGrantRecord(63, 51, "records", tenantId: 7, customPermissionName: "approve"),
            new PermissionGrantRecord(64, 51, "records", tenantId: 7, extensionFlags: 4)
        ]);
        await manager.ReplaceRoleGrantsAsync(51, 7,
        [
            new PermissionGrantRecord(61, 51, "records", tenantId: 7,
                flags: (byte)(PermissionFlags.Query | PermissionFlags.Delete)),
            new PermissionGrantRecord(62, 51, "records", tenantId: 7, customPermissionName: "export"),
            new PermissionGrantRecord(63, 51, "records", tenantId: 7, customPermissionName: "approve"),
            new PermissionGrantRecord(64, 51, "records", tenantId: 7, extensionFlags: 4)
        ]);

        var provider = new EfCorePermissionGrantProvider<PermissionDbContext>(context);
        var service = new PermissionAuthorizationService(
            new PermissionDefinitionRegistry([new TestPermissionDefinitionProvider()]), provider);

        Assert.True(await service.IsGrantedAsync(200, 7, "records", PermissionFlags.Query));
        Assert.True(await service.IsGrantedAsync(200, 7, "records", PermissionFlags.Delete));
        Assert.False(await service.IsGrantedAsync(200, 7, "records", PermissionFlags.Modify));
        Assert.True(await service.IsCustomPermissionGrantedAsync(200, 7, "records", "export"));
        Assert.True(await service.IsCustomPermissionGrantedAsync(200, 7, "records", "approve"));
        Assert.True(await service.IsExtensionPermissionGrantedAsync(200, 7, "records", 4));
        Assert.Equal(4, await context.Grants.CountAsync(grant => grant.RoleId == 51));
    }

    [Fact]
    public async Task ClientScopedRoleReplacementDoesNotRemoveAnotherClientsAssignmentsOrGrants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseSqlite(connection).Options;
        await using var context = CreateContext(options, 7);
        await context.Database.EnsureCreatedAsync();
        context.Roles.AddRange(
            new PermissionRole(81, "reader", clientCode: "alpha"),
            new PermissionRole(82, "reader", clientCode: "beta"));
        context.Grants.AddRange(
            new PermissionGrantRecord(83, 81, "records", tenantId: 7, flags: (byte)PermissionFlags.Query),
            new PermissionGrantRecord(84, 82, "records", tenantId: 7, flags: (byte)PermissionFlags.Query));
        await context.SaveChangesAsync();

        var manager = new EfCorePermissionGrantManager<PermissionDbContext>(context);
        await manager.AssignSubjectRolesAsync(200, 7, "alpha", [81]);
        await manager.AssignSubjectRolesAsync(200, 7, "beta", [82]);
        await manager.AssignSubjectRolesAsync(200, 7, "alpha", []);

        Assert.Equal(1, await context.SubjectRoles.CountAsync(assignment => assignment.SubjectId == 200));
        Assert.Equal("beta", await context.SubjectRoles
            .Where(assignment => assignment.SubjectId == 200)
            .Select(assignment => assignment.ClientCode)
            .SingleAsync());

        var provider = new EfCorePermissionGrantProvider<PermissionDbContext>(context);
        var catalog = new PermissionDefinitionRegistry(
        [
            new TestPermissionDefinitionProvider("alpha"),
            new TestPermissionDefinitionProvider("beta")
        ]);
        var service = new PermissionAuthorizationService(catalog, provider);

        Assert.False(await service.IsGrantedAsync(200, 7, "alpha", "records", PermissionFlags.Query));
        Assert.True(await service.IsGrantedAsync(200, 7, "beta", "records", PermissionFlags.Query));
    }

    [Fact]
    public async Task LegacyGrantProviderFailsClosedForNonEmptyClientCode()
    {
        var service = new PermissionAuthorizationService(
            new PermissionDefinitionRegistry([new TestPermissionDefinitionProvider("alpha")]),
            new LegacyGrantProvider());

        Assert.False(await service.IsGrantedAsync(200, 7, "alpha", "records", PermissionFlags.Query));
    }

    [Fact]
    public async Task PermissionModuleMetadataPersistsByTenantAndClientWithoutBecomingAGrant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseSqlite(connection).Options;
        await using var context = CreateContext(options, 7);
        await context.Database.EnsureCreatedAsync();
        context.Set<PermissionModule>().Add(new PermissionModule(
            91,
            "alpha",
            "records",
            "Records",
            tenantId: 7,
            availableFlags: (byte)(PermissionFlags.Query | PermissionFlags.Custom),
            customPermissionsJson: "[\"export\"]"));
        await context.SaveChangesAsync();

        var metadata = await context.Set<PermissionModule>().SingleAsync();
        var service = new PermissionAuthorizationService(
            new PermissionDefinitionRegistry([new TestPermissionDefinitionProvider("alpha")]),
            new EfCorePermissionGrantProvider<PermissionDbContext>(context));

        Assert.Equal("alpha", metadata.ClientCode);
        Assert.Equal("[\"export\"]", metadata.CustomPermissionsJson);
        Assert.False(await service.IsGrantedAsync(200, 7, "alpha", "records", PermissionFlags.Query));
    }

    [Fact]
    public async Task GrantTableRejectsEmptyPermissionPayloads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PermissionDbContext>().UseSqlite(connection).Options;
        await using var context = CreateContext(options, 7);
        await context.Database.EnsureCreatedAsync();
        context.Roles.Add(new PermissionRole(51, "operator"));
        context.Grants.Add(new PermissionGrantRecord(71, 51, "records", flags: (byte)PermissionFlags.Query));
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE pubsoft_permission_grant SET Flags = 0 WHERE Id = 71"));

        var provider = new EfCorePermissionGrantProvider<PermissionDbContext>(context);
        var grants = await provider.GetGrantsAsync(42, 7);
        Assert.Empty(grants);
    }

    [Fact]
    public void DomainGrantRejectsEmptyPayload()
    {
        Assert.Throws<ArgumentException>(() => new PermissionGrantRecord(72, 51, "records"));
    }

    [Fact]
    public void PermissionEntitiesValidateClientCodeOnMutation()
    {
        var role = new PermissionRole(101, "reader");
        var assignment = new SubjectPermissionRole(102, 42, 101);

        Assert.Throws<ArgumentException>(() => role.ClientCode = " shop ");
        Assert.Throws<ArgumentException>(() => assignment.ClientCode = new string('x', 65));
        role.ClientCode = "shop";
        assignment.ClientCode = "shop";

        Assert.Equal("shop", role.ClientCode);
        Assert.Equal("shop", assignment.ClientCode);
    }

    [Fact]
    public void PermissionModuleRejectsUnknownFlagsWithoutPartiallyUpdating()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PermissionModule(
            111, "shop", "records", "Records", availableFlags: 64));

        var module = new PermissionModule(112, "shop", "records", "Records", availableFlags: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => module.Update(
            "Updated records", null, 128, null, null));

        Assert.Equal("Records", module.DisplayName);
        Assert.Equal((byte)1, module.AvailableFlags);
    }

    [Fact]
    public void PermissionModuleRejectsCustomPermissionJsonExceedingMappedColumnLength()
    {
        var oversizedJson = "[\"" + new string('x', 4000) + "\"]";
        Assert.Throws<ArgumentException>(() => new PermissionModule(
            113, "shop", "records", "Records", customPermissionsJson: oversizedJson));

        var module = new PermissionModule(114, "shop", "records", "Records", customPermissionsJson: "[]");
        Assert.Throws<ArgumentException>(() => module.Update(
            "Updated records", null, null, oversizedJson, null));

        Assert.Equal("Records", module.DisplayName);
        Assert.Equal("[]", module.CustomPermissionsJson);
    }

    private static PermissionDbContext CreateContext(DbContextOptions<PermissionDbContext> options, long? tenantId) =>
        new(options, new TestTenantContext(tenantId));

    private sealed class PermissionDbContext(
        DbContextOptions<PermissionDbContext> options,
        ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext)
    {
        public DbSet<PermissionRole> Roles => Set<PermissionRole>();
        public DbSet<SubjectPermissionRole> SubjectRoles => Set<SubjectPermissionRole>();
        public DbSet<PermissionGrantRecord> Grants => Set<PermissionGrantRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftPermissionGrants();
        }
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId { get; } = tenantId;
    }

    private sealed class TestPermissionDefinitionProvider(string clientCode = "") : IPermissionDefinitionProvider
    {
        public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
        [
            new PermissionDefinition
            {
                ClientCode = clientCode,
                ModuleKey = "records",
                Flags = PermissionFlags.Query | PermissionFlags.Modify | PermissionFlags.Delete | PermissionFlags.Custom,
                DisplayName = "Records"
            },
            new PermissionDefinition
            {
                ClientCode = clientCode,
                ModuleKey = "records",
                Flags = PermissionFlags.Custom,
                DisplayName = "Record extensions",
                IsExtPermission = true,
                ExtFlag = 4
            }
        ];
    }

    private sealed class LegacyGrantProvider : IPermissionGrantProvider
    {
        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
            long subjectId,
            long? tenantId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PermissionGrant>>(
                [new PermissionGrant(tenantId, "records", PermissionFlags.Query)]);
    }
}