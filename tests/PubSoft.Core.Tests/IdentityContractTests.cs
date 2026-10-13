using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Identity;
using Xunit;

namespace PubSoft.Core.Tests;

public class IdentityContractTests
{
    [Fact]
    public void User_contract_exposes_a_read_only_profile_without_credentials()
    {
        IIdentityUser user = new TestIdentityUser(17, "case-sensitive", "Display name", "+12025550100");

        Assert.Equal(17, user.Id);
        Assert.Equal("case-sensitive", user.UserName);
        Assert.Equal("Display name", user.Name);
        Assert.Equal("+12025550100", user.PhoneNumber);
        Assert.All(typeof(IIdentityUser).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(typeof(IIdentityUser).GetProperties(), property => property.Name == "PasswordHash");
    }

    [Fact]
    public void Snapshot_copies_profile_values_and_is_read_only()
    {
        var source = new MutableIdentityUser(17, "original", "Original name", "+12025550100");

        var snapshot = IdentityUserSnapshot.FromUser(source);
        source.UserName = "changed";
        source.Name = "Changed name";
        source.PhoneNumber = null;

        Assert.Equal("original", snapshot.UserName);
        Assert.Equal("Original name", snapshot.Name);
        Assert.Equal("+12025550100", snapshot.PhoneNumber);
        Assert.All(typeof(IdentityUserSnapshot).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Snapshot_rejects_null_user()
    {
        Assert.Throws<ArgumentNullException>(() => IdentityUserSnapshot.FromUser(null!));
    }

    [Fact]
    public void IdentityUserNormalizesUsernameAndRetainsTenantScope()
    {
        var user = new IdentityUser(17, "  Jane.Doe ", tenantId: 8);

        Assert.Equal("Jane.Doe", user.UserName);
        Assert.Equal("JANE.DOE", user.NormalizedUserName);
        Assert.Equal(8, user.TenantId);
        Assert.Equal("Jane.Doe", user.Name);

        user.SetUserName("jANE.dOE");
        Assert.Equal("JANE.DOE", user.NormalizedUserName);
    }

    [Fact]
    public void PasswordHashUpdatesRotateStampAndSnapshotsNeverExposeCredentials()
    {
        var user = new IdentityUser(17, "jane");
        var initialStamp = user.SecurityStamp;

        user.SetPasswordHash("opaque-hash-v1");
        var passwordStamp = user.SecurityStamp;
        var snapshot = IdentityUserSnapshot.FromUser(user);
        user.SetPasswordHash("opaque-hash-v2");

        Assert.NotEqual(initialStamp, passwordStamp);
        Assert.NotEqual(passwordStamp, user.SecurityStamp);
        Assert.Null(typeof(IdentityUserSnapshot).GetProperty(nameof(IdentityUser.PasswordHash)));
        Assert.Null(typeof(IIdentityUser).GetProperty(nameof(IdentityUser.PasswordHash)));
        Assert.Equal("jane", snapshot.UserName);
    }

    [Fact]
    public void IdentityUserRejectsInvalidIdentifiersAndBlankUsername()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityUser(0, "jane"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityUser(17, "jane", tenantId: 0));
        Assert.Throws<ArgumentException>(() => new IdentityUser(17, " "));
    }

    [Fact]
    public void IdentityUserRejectsValuesThatExceedEfColumnLimits()
    {
        var user = new IdentityUser(17, "jane");

        Assert.Throws<ArgumentException>(() => user.SetUserName(new string('u', 257)));
        Assert.Throws<ArgumentException>(() => user.UpdateProfile(new string('n', 257), null, false));
        Assert.Throws<ArgumentException>(() => user.UpdateProfile("Jane", new string('1', 33), false));
        Assert.Throws<ArgumentException>(() => user.SetPasswordHash(new string('h', 513)));
        Assert.Equal("jane", user.UserName);
        Assert.Equal("jane", user.Name);
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task EfIdentityMappingEnforcesTenantUsernameUniquenessAndSoftDeleteFiltering()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection).Options;
        await using var context = new IdentityDbContext(options, new TestTenantContext(7));
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(
            new IdentityUser(101, "jane.doe", tenantId: 7),
            new IdentityUser(102, "jane.doe", tenantId: 8));
        await context.SaveChangesAsync();

        context.Users.Add(new IdentityUser(103, " JANE.DOE ", tenantId: 7));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(context.Users.Local.Single(user => user.Id == 103)).State = EntityState.Detached;

        var tenantSevenUser = await context.Users.SingleAsync(user => user.Id == 101);
        tenantSevenUser.IsDeleted = true;
        await context.SaveChangesAsync();

        Assert.Empty(await context.Users.Where(user => user.Id == 101).ToListAsync());
        Assert.True(await context.Database.CanConnectAsync());

        await using var otherTenant = new IdentityDbContext(options, new TestTenantContext(8));
        Assert.Single(await otherTenant.Users.Where(user => user.Id == 102).ToListAsync());
    }

    private sealed class IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        ITenantContext tenantContext) : PubSoftDbContext(options, tenantContext)
    {
        public DbSet<IdentityUser> Users => Set<IdentityUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftIdentityUsers();
        }
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId { get; } = tenantId;
    }

    private sealed record TestIdentityUser(long Id, string UserName, string Name, string? PhoneNumber) : IIdentityUser;

    private sealed class MutableIdentityUser(long id, string userName, string name, string? phoneNumber) : IIdentityUser
    {
        public long Id { get; } = id;
        public string UserName { get; set; } = userName;
        public string Name { get; set; } = name;
        public string? PhoneNumber { get; set; } = phoneNumber;
    }
}