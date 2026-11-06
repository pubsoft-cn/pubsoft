using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Migrations;
using PubSoft.Messaging.Outbox;
using PubSoft.Scheduling;
using PubSoft.Scheduling.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MySqlProviderIntegrationTests
{
    [MySqlFact]
    public async Task MySql84_CreatesOutboxInboxAndSchedulerStateSchema()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        await using var context = new IntegrationDbContext(options, new TestTenantContext(73));

        await context.Database.EnsureCreatedAsync();

        Assert.True(await context.Database.CanConnectAsync());
        Assert.Empty(await context.OutboxMessages.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await context.Set<InboxReceipt>().IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await context.Set<SchedulerStateRecord>().IgnoreQueryFilters().ToListAsync());
    }

    [MySqlFact]
    public async Task MySql84_StandardMigrationExecutesOperationsBeforeRecordingHistory()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<MigrationProbeDbContext>()
            .UseMySQL(database.ConnectionString)
            .Options;
        await using var context = new MigrationProbeDbContext(options);

        await new EfCoreDatabaseSchemaMigrator<MigrationProbeDbContext>(context).MigrateAsync();

        Assert.Contains(
            await context.Database.GetAppliedMigrationsAsync(),
            migration => migration == MigrationProbe.Id);
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `MigrationProbe`";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [MySqlFact]
    public async Task MySql84_ConcurrentOutboxClaimsDoNotLeaseTheSameMessageTwice()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        await using (var setup = new IntegrationDbContext(options, new TestTenantContext(73)))
        {
            await setup.Database.EnsureCreatedAsync();
            await new EfCoreOutboxPublisher<IntegrationDbContext>(setup)
                .PublishAsync(new TestEvent(1));
            await setup.SaveChangesAsync();
        }

        async Task<IReadOnlyList<OutboxMessage>> ClaimAsync()
        {
            await using var context = new IntegrationDbContext(options, new TestTenantContext(73));
            return await new EfCoreOutboxStore<IntegrationDbContext>(context).ClaimBatchAsync();
        }

        var claims = (await Task.WhenAll(ClaimAsync(), ClaimAsync())).SelectMany(batch => batch).ToArray();

        Assert.Single(claims);
        Assert.Equal(OutboxMessageStatus.Processing, claims[0].Status);
        Assert.NotNull(claims[0].LeaseToken);
    }

    [MySqlFact]
    public async Task MySql84_ExpiredOutboxLeaseCannotCompleteAfterReclaim()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var outboxOptions = new OutboxOptions { LeaseDuration = TimeSpan.FromMinutes(1) };
        await using var context = new IntegrationDbContext(options, new TestTenantContext(73));
        await context.Database.EnsureCreatedAsync();
        await new EfCoreOutboxPublisher<IntegrationDbContext>(context, outboxOptions, clock)
            .PublishAsync(new TestEvent(7));
        await context.SaveChangesAsync();
        var store = new EfCoreOutboxStore<IntegrationDbContext>(context, outboxOptions, clock);

        var firstClaim = Assert.Single(await store.ClaimBatchAsync());
        var firstLeaseToken = Assert.IsType<Guid>(firstClaim.LeaseToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await store.MarkDeliveredAsync(firstClaim.Id, firstLeaseToken));
        Assert.False(await store.MarkFailedAsync(firstClaim.Id, firstLeaseToken, "ExpiredFailure"));

        var reclaimed = Assert.Single(await store.ClaimBatchAsync());
        var currentLeaseToken = Assert.IsType<Guid>(reclaimed.LeaseToken);

        Assert.NotEqual(firstLeaseToken, currentLeaseToken);
        Assert.False(await store.MarkDeliveredAsync(reclaimed.Id, firstLeaseToken));
        Assert.False(await store.MarkFailedAsync(reclaimed.Id, firstLeaseToken, "StaleFailure"));
        Assert.True(await store.MarkDeliveredAsync(reclaimed.Id, currentLeaseToken));

        await using var verify = new IntegrationDbContext(options, new TestTenantContext(73));
        var persisted = await verify.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Delivered, persisted.Status);
        Assert.Equal(2, persisted.AttemptCount);
        Assert.NotNull(persisted.DeliveredAt);
    }

    [MySqlFact]
    public async Task MySql84_ConcurrentInboxDeliveryRunsTheHandlerOnlyOnce()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        await using (var setup = new IntegrationDbContext(options, new TestTenantContext(73)))
            await setup.Database.EnsureCreatedAsync();

        var messageId = Guid.NewGuid();
        var handlerCalls = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> ProcessAsync(int orderId)
        {
            await using var context = new IntegrationDbContext(options, new TestTenantContext(73));
            await start.Task;
            return await new EfCoreInboxMessageProcessor<IntegrationDbContext>(context)
                .ProcessOnceAsync("mysql-orders", messageId, async cancellationToken =>
                {
                    Interlocked.Increment(ref handlerCalls);
                    context.Orders.Add(new IntegrationOrder { Id = orderId, Name = "processed" });
                    await Task.Delay(10, cancellationToken);
                });
        }

        var first = ProcessAsync(81);
        var second = ProcessAsync(82);
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, processed => processed);
        Assert.Single(results, processed => !processed);
        Assert.Equal(1, handlerCalls);
        await using var verify = new IntegrationDbContext(options, new TestTenantContext(73));
        Assert.Single(await verify.Orders.ToListAsync());
        Assert.Single(await verify.Set<InboxReceipt>().ToListAsync());
    }

    [MySqlFact]
    public async Task MySql84_InboxHandlerFailureRollsBackAndAllowsRetry()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        await using var context = new IntegrationDbContext(options, new TestTenantContext(73));
        await context.Database.EnsureCreatedAsync();
        var processor = new EfCoreInboxMessageProcessor<IntegrationDbContext>(context);
        var messageId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessOnceAsync(
            "mysql-orders",
            messageId,
            _ =>
            {
                context.Orders.Add(new IntegrationOrder { Id = 91, Name = "rolled back" });
                throw new InvalidOperationException("handler failed");
            }));

        var succeeded = await processor.ProcessOnceAsync("mysql-orders", messageId, _ =>
        {
            context.Orders.Add(new IntegrationOrder { Id = 92, Name = "retried" });
            return Task.CompletedTask;
        });

        Assert.True(succeeded);
        Assert.Equal("retried", Assert.Single(await context.Orders.ToListAsync()).Name);
        Assert.Single(await context.Set<InboxReceipt>().ToListAsync());
    }

    [MySqlFact]
    public async Task MySql84_SchedulerStateConcurrentInsertUpdatesTheWinningRow()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        await using (var setup = new IntegrationDbContext(options, new TestTenantContext(73)))
            await setup.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddDbContext<IntegrationDbContext>(builder => builder.UseMySQL(database.ConnectionString));
        services.AddSingleton<ITenantContext>(new TestTenantContext(73));
        services.AddPubSoftEfCoreSchedulerStateStore<IntegrationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IStateStore>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task SetValueAsync(int value)
        {
            await start.Task;
            await store.SetCustomStateAsync("concurrent", new StateValue(value), null, default);
        }

        var first = SetValueAsync(1);
        var second = SetValueAsync(2);
        start.SetResult();
        await Task.WhenAll(first, second);

        var state = await store.GetCustomStateAsync<StateValue>("concurrent", default);
        Assert.NotNull(state);
        Assert.Contains(state.Value, new[] { 1, 2 });
    }

    [MySqlFact]
    public async Task MySql84_SchedulerStateConcurrentUpdatesKeepOneCompleteValue()
    {
        await using var database = await IsolatedMySqlDatabase.CreateAsync();
        var options = CreateOptions(database.ConnectionString);
        await using (var setup = new IntegrationDbContext(options, new TestTenantContext(73)))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<IntegrationDbContext>(builder => builder.UseMySQL(database.ConnectionString));
        services.AddSingleton<ITenantContext>(new TestTenantContext(73));
        services.AddPubSoftEfCoreSchedulerStateStore<IntegrationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IStateStore>();
        await store.SetCustomStateAsync("updates", new StateValue(-1), null, default);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writtenValues = Enumerable.Range(0, 16)
            .Select(writer => new StateValue(writer))
            .ToArray();
        var updates = writtenValues.Select(async value =>
        {
            await start.Task;
            await store.SetCustomStateAsync("updates", value, null, default);
        });

        start.SetResult();
        await Task.WhenAll(updates);

        var state = await store.GetCustomStateAsync<StateValue>("updates", default);
        Assert.NotNull(state);
        Assert.Contains(state, writtenValues);
        await using var verify = new IntegrationDbContext(options, new TestTenantContext(73));
        Assert.Equal(1, await verify.Set<SchedulerStateRecord>().CountAsync());
    }

    private static DbContextOptions<IntegrationDbContext> CreateOptions(string connectionString)
        => new DbContextOptionsBuilder<IntegrationDbContext>().UseMySQL(connectionString).Options;

    private sealed class IntegrationDbContext(
        DbContextOptions<IntegrationDbContext> options,
        ITenantContext tenantContext) : PubSoftDbContext(options, tenantContext)
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<IntegrationOrder> Orders => Set<IntegrationOrder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePubSoftOutbox();
            modelBuilder.ConfigurePubSoftSchedulerState();
            modelBuilder.Entity<IntegrationOrder>().HasKey(order => order.Id);
        }
    }

    private sealed class MigrationProbeDbContext(DbContextOptions<MigrationProbeDbContext> options)
        : DbContext(options);

    [DbContext(typeof(MigrationProbeDbContext))]
    [Migration(Id)]
    public sealed class MigrationProbe : Migration
    {
        public const string Id = "20261001000100_MySqlMigrationProbe";

        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.CreateTable(
                name: "MigrationProbe",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_MigrationProbe", row => row.Id));

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DropTable("MigrationProbe");
    }

    private sealed class IntegrationOrder : IMultiTenant
    {
        public int Id { get; set; }
        public long? TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed record TestEvent(int Id);
    private sealed record StateValue(int Value);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId => tenantId;
    }

    private sealed class MySqlFactAttribute : FactAttribute
    {
        public MySqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_MYSQL")))
                Skip = "Set PUBSOFT_MYSQL to run MySQL provider integration tests.";
        }
    }

    private sealed class IsolatedMySqlDatabase : IAsyncDisposable
    {
        private readonly MySqlConnectionStringBuilder _adminConnectionString;
        private readonly string _schema;

        private IsolatedMySqlDatabase(string schema, MySqlConnectionStringBuilder adminConnectionString)
        {
            _schema = schema;
            _adminConnectionString = adminConnectionString;
            var testConnectionString = new MySqlConnectionStringBuilder(adminConnectionString.ConnectionString)
            {
                Database = schema
            };
            ConnectionString = testConnectionString.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<IsolatedMySqlDatabase> CreateAsync()
        {
            var configuredConnectionString = Environment.GetEnvironmentVariable("PUBSOFT_MYSQL");
            if (string.IsNullOrWhiteSpace(configuredConnectionString))
                throw new InvalidOperationException("PUBSOFT_MYSQL is required.");

            var adminConnectionString = new MySqlConnectionStringBuilder(configuredConnectionString)
            {
                Database = string.Empty
            };
            var schema = $"pubsoft_it_{Guid.NewGuid():N}";
            await using var connection = new MySqlConnection(adminConnectionString.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4";
            await command.ExecuteNonQueryAsync();
            return new IsolatedMySqlDatabase(schema, adminConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new MySqlConnection(_adminConnectionString.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{_schema}`";
            await command.ExecuteNonQueryAsync();
        }
    }
}