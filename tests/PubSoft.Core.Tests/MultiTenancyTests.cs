using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore;
using PubSoft.MultiTenancy;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MultiTenancyTests
{
    [Fact]
    public async Task TenantModelNormalizesFieldsAndPersistsWithUniqueCodeConstraint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(connection).Options;
        var tenant = new Tenant(1, " store-a ", " Store A ", "  Store Alpha  ");
        tenant.Update(" Updated Store ", null, "  tenant description  ");
        tenant.SetEnabled(false);

        await using (var context = new TenantDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var restored = await context.Tenants.SingleAsync();
            Assert.Equal("store-a", restored.Code);
            Assert.Equal("Updated Store", restored.Name);
            Assert.Equal("Updated Store", restored.DisplayName);
            Assert.Equal("tenant description", restored.Description);
            Assert.False(restored.IsEnabled);

            context.Tenants.Add(new Tenant(2, "store-a", "Duplicate"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public void TenantRejectsInvalidRequiredAndOverlongFields()
    {
        Assert.Throws<ArgumentException>(() => new Tenant(1, " ", "Tenant"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tenant(1, new string('c', 65), "Tenant"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tenant(1, "valid", new string('n', 101)));

        var tenant = new Tenant(1, "valid", "Tenant");
        Assert.Throws<ArgumentOutOfRangeException>(() => tenant.Update("Tenant", null, new string('d', 201)));
    }

    private sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
    {
        public DbSet<Tenant> Tenants => Set<Tenant>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ConfigurePubSoftTenants();
    }
}