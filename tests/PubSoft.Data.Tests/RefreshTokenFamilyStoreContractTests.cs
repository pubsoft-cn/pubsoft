using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Core;
using PubSoft.Security;
using PubSoft.Security.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Data.Tests;

public sealed class RefreshTokenFamilyStoreContractTests
{
    [Fact]
    public async Task EfCoreStore_ConcurrentConsumesResolveToSingleWinner()
    {
        await using var fixture = await SqliteSecurityFixture.CreateAsync();
        var store = fixture.CreateStore();
        var hash = await SaveTokenAsync(store, "ef-race");

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => store.TryConsumeAsync(hash).AsTask()));

        Assert.Single(results, result => result == RefreshConsumeResult.Consumed);
        Assert.Equal(15, results.Count(result => result == RefreshConsumeResult.AlreadyConsumed));
    }

    [Fact]
    public async Task EfCoreStore_RevokedFamilyCannotConsume()
    {
        await using var fixture = await SqliteSecurityFixture.CreateAsync();
        var store = fixture.CreateStore();
        var hash = await SaveTokenAsync(store, "ef-revoked");
        var record = (await store.GetByHashAsync(hash))!;

        await store.RevokeFamilyAsync(record.FamilyId, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(RefreshConsumeResult.FamilyRevoked, await store.TryConsumeAsync(hash));
        Assert.False((await store.GetByHashAsync(hash))!.Consumed);
    }

    private static async Task<string> SaveTokenAsync(IRefreshTokenFamilyStore store, string familyId)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = TokenDenyIds.ForToken("token-" + familyId + Guid.NewGuid().ToString("N"));
        await store.SaveAsync(new RefreshTokenFamilyRecord(
            hash,
            familyId,
            "contract-subject",
            now,
            now.AddMinutes(10),
            now.AddHours(1),
            Consumed: false));
        return hash;
    }

    private sealed class SqliteSecurityFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly ServiceProvider _provider;

        private SqliteSecurityFixture(SqliteConnection keepAlive, ServiceProvider provider)
        {
            _keepAlive = keepAlive;
            _provider = provider;
        }

        public static async Task<SqliteSecurityFixture> CreateAsync()
        {
            var connectionString = $"Data Source=refresh-contract-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keepAlive = new SqliteConnection(connectionString);
            await keepAlive.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<SecurityContractDbContext>(options => options.UseSqlite(connectionString));
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<SecurityContractDbContext>().Database.EnsureCreatedAsync();
            return new SqliteSecurityFixture(keepAlive, provider);
        }

        public EfCoreRefreshTokenFamilyStore<SecurityContractDbContext> CreateStore()
            => new(_provider.GetRequiredService<IServiceScopeFactory>(), new SnowflakeIdGenerator(1));

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _keepAlive.DisposeAsync();
        }
    }

    private sealed class SecurityContractDbContext(DbContextOptions<SecurityContractDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftSecurityState();
        }
    }
}
