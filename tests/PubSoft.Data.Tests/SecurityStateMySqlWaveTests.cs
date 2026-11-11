using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using PubSoft.Core;
using PubSoft.Security;
using PubSoft.Security.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Data.Tests;

/// <summary>EF security stores against real MySQL: atomic consume, restart persistence, prune.</summary>
public sealed class SecurityStateMySqlWaveTests
{
    private sealed class MySqlFactAttribute : FactAttribute
    {
        public MySqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_MYSQL")))
                Skip = "Set PUBSOFT_MYSQL to run MySQL provider integration tests.";
        }
    }

    private sealed class SecurityDbContext(DbContextOptions<SecurityDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftSecurityState();
        }
    }

    private static async Task<(ServiceProvider Provider, string Schema)> CreateProviderAsync()
    {
        var configured = Environment.GetEnvironmentVariable("PUBSOFT_MYSQL")
            ?? throw new InvalidOperationException("PUBSOFT_MYSQL is required.");
        var schema = $"pubsoft_sec_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(configured) { Database = string.Empty };
        await using (var connection = new MySqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4";
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new MySqlConnectionStringBuilder(configured) { Database = schema }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDbContext<SecurityDbContext>(builder => builder.UseMySQL(connectionString));
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Database.EnsureCreatedAsync();
        return (provider, schema);
    }

    private static EfCoreRefreshTokenFamilyStore<SecurityDbContext> FamilyStore(ServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), new SnowflakeIdGenerator(1));

    private static EfCoreTokenDenylist<SecurityDbContext> Denylist(ServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), new SnowflakeIdGenerator(1));

    [MySqlFact]
    public async Task RotateThenReuseRevokesFamily()
    {
        await using var provider = (await CreateProviderAsync()).Provider;
        var service = new RefreshTokenFamilyService(FamilyStore(provider), new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.FromMinutes(10) });

        var issue = await service.IssueAsync("subject-mysql");
        var rotated = await service.RotateAsync(issue.Token);
        Assert.Equal(RefreshRotationVerdict.Rotated, rotated.Verdict);

        var replay = await service.RotateAsync(issue.Token);
        Assert.Equal(RefreshRotationVerdict.ReuseDetected, replay.Verdict);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(rotated.NewToken!)).Verdict);
    }

    [MySqlFact]
    public async Task ConcurrentConsumesResolveToSingleWinner()
    {
        await using var provider = (await CreateProviderAsync()).Provider;
        var store = FamilyStore(provider);
        var service = new RefreshTokenFamilyService(store);
        var issue = await service.IssueAsync("subject-race");
        var hash = TokenDenyIds.ForToken(issue.Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.TryConsumeAsync(hash).AsTask()));
        Assert.Equal(1, results.Count(result => result == RefreshConsumeResult.Consumed));
        Assert.All(results.Where(result => result != RefreshConsumeResult.Consumed), result => Assert.Equal(RefreshConsumeResult.AlreadyConsumed, result));
    }

    [MySqlFact]
    public async Task RevokeSubjectKillsAllFamilies()
    {
        await using var provider = (await CreateProviderAsync()).Provider;
        var service = new RefreshTokenFamilyService(FamilyStore(provider));
        var a = await service.IssueAsync("subject-all");
        var b = await service.IssueAsync("subject-all");
        var other = await service.IssueAsync("subject-other");

        Assert.Equal(2, await service.RevokeSubjectAsync("subject-all"));
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(a.Token)).Verdict);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(b.Token)).Verdict);
        Assert.Equal(RefreshRotationVerdict.Rotated, (await service.RotateAsync(other.Token)).Verdict);
    }

    [MySqlFact]
    public async Task DenylistPersistsAcrossStoreInstances()
    {
        await using var provider = (await CreateProviderAsync()).Provider;
        var id = TokenDenyIds.ForToken("restart-sensitive-token");
        await Denylist(provider).DenyAsync(new TokenDenyEntry(id, "subject-1", DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30), "logout"));

        // a fresh store instance over a fresh scope = post-restart read
        Assert.True(await Denylist(provider).IsDeniedAsync(id));
        Assert.False(await Denylist(provider).IsDeniedAsync(TokenDenyIds.ForToken("other-token")));
    }

    [MySqlFact]
    public async Task DenylistEntryStopsMatchingAfterExpiry()
    {
        await using var provider = (await CreateProviderAsync()).Provider;
        var id = TokenDenyIds.ForToken("expiring-token");
        var denylist = Denylist(provider);
        await denylist.DenyAsync(new TokenDenyEntry(id, null, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2), null));
        Assert.True(await denylist.IsDeniedAsync(id));
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.False(await denylist.IsDeniedAsync(id));
    }

    [MySqlFact]
    public async Task PruneExpiredDeletesDeadRows()
    {
        await using var provider = (await CreateProviderAsync()).Provider;
        var store = FamilyStore(provider);
        var denylist = Denylist(provider);
        var service = new RefreshTokenFamilyService(store, new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.FromMilliseconds(500) });
        await service.IssueAsync("subject-prune");
        await denylist.DenyAsync(new TokenDenyEntry("prune-id", null, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(500), null));
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(1, await denylist.PruneExpiredAsync());
        Assert.Equal(1, await store.PruneExpiredAsync());
    }

    [Fact]
    public void RegistrationsSwapContractImplementations()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SecurityDbContext>(builder => builder.UseMySQL("Server=localhost;Database=x;User=x;Password=x"));
        services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(1));
        services.AddPubSoftEfCoreRefreshTokenFamilyStore<SecurityDbContext>();
        services.AddPubSoftEfCoreTokenDenylist<SecurityDbContext>();
        var provider = services.BuildServiceProvider();
        Assert.IsType<EfCoreRefreshTokenFamilyStore<SecurityDbContext>>(provider.GetRequiredService<IRefreshTokenFamilyStore>());
        Assert.IsType<EfCoreTokenDenylist<SecurityDbContext>>(provider.GetRequiredService<ITokenDenylist>());
    }
}
