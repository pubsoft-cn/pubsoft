using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Core;
using PubSoft.Security;
using PubSoft.Security.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var services = new ServiceCollection();
services.AddDbContext<SmokeDbContext>(options => options.UseSqlite(connection));
services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(1));
services.AddPubSoftEfCoreRefreshTokenFamilyStore<SmokeDbContext>();
services.AddPubSoftEfCoreTokenDenylist<SmokeDbContext>();

await using var provider = services.BuildServiceProvider();
await using (var setupScope = provider.CreateAsyncScope())
    await setupScope.ServiceProvider.GetRequiredService<SmokeDbContext>().Database.EnsureCreatedAsync();

var now = DateTimeOffset.UtcNow;
var familyStore = provider.GetRequiredService<IRefreshTokenFamilyStore>();
await familyStore.SaveAsync(new RefreshTokenFamilyRecord(
    "hash-1", "family-1", "subject-1", now, now.AddHours(1), now.AddDays(7), Consumed: false));
var fetched = await familyStore.GetByHashAsync("hash-1");
var firstConsume = await familyStore.TryConsumeAsync("hash-1");
var secondConsume = await familyStore.TryConsumeAsync("hash-1");
if (fetched is not { FamilyId: "family-1" }
    || firstConsume != RefreshConsumeResult.Consumed
    || secondConsume != RefreshConsumeResult.AlreadyConsumed
    || familyStore is not EfCoreRefreshTokenFamilyStore<SmokeDbContext>)
    throw new InvalidOperationException("Security EF refresh family consumer check failed.");

var denylist = provider.GetRequiredService<ITokenDenylist>();
await denylist.DenyAsync(new TokenDenyEntry("token-id-1", "subject-1", now.AddMinutes(15), "consumer-smoke"));
var denied = await denylist.IsDeniedAsync("token-id-1");
var unknown = await denylist.IsDeniedAsync("token-id-other");
if (!denied || unknown || denylist is not EfCoreTokenDenylist<SmokeDbContext>)
    throw new InvalidOperationException("Security EF token denylist consumer check failed.");

Console.WriteLine("Isolated PubSoft.Security.EntityFrameworkCore consumer persistence check passed.");

sealed class SmokeDbContext(DbContextOptions<SmokeDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigurePubSoftSecurityState();
    }
}
