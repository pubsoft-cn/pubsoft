using System.Transactions;
using DataIsolationLevel = System.Data.IsolationLevel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EfCoreTransactionExecutorTests
{
    private sealed class Widget
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ExecutorDbContext(DbContextOptions<ExecutorDbContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>().HasKey(widget => widget.Id);
            modelBuilder.Entity<Widget>().Property(widget => widget.Id).ValueGeneratedNever();
        }
    }

    private static ExecutorDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ExecutorDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ExecutorDbContext(options);
    }

    [Fact]
    public async Task ExecutorCommitsSingleContextOperation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        var result = await EfCoreTransactionExecutor.ExecuteInTransactionAsync(
            context,
            async (db, cancellationToken) =>
            {
                db.Widgets.Add(new Widget { Id = 101, Name = "root" });
                await db.SaveChangesAsync(cancellationToken);
                return 7;
            });

        Assert.Equal(7, result);
        Assert.Equal(1, await context.Widgets.CountAsync());
    }

    [Fact]
    public async Task NestedCallJoinsRootTransactionAndRollsBackWithIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var nestedCalled = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => EfCoreTransactionExecutor.ExecuteInTransactionAsync(
            context,
            async (db, cancellationToken) =>
            {
                db.Widgets.Add(new Widget { Id = 102, Name = "root" });
                await db.SaveChangesAsync(cancellationToken);
                await EfCoreTransactionExecutor.ExecuteInTransactionAsync(
                    db,
                    async (nestedDb, nestedCancellationToken) =>
                    {
                        nestedCalled = true;
                        nestedDb.Widgets.Add(new Widget { Id = 103, Name = "nested" });
                        await nestedDb.SaveChangesAsync(nestedCancellationToken);
                        throw new InvalidOperationException("force root rollback");
                    },
                    cancellationToken: cancellationToken);
            }));

        Assert.True(nestedCalled);
        Assert.Equal(0, await context.Widgets.CountAsync());
    }

    [Fact]
    public async Task NestedCallCannotChangeIsolationLevel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => EfCoreTransactionExecutor.ExecuteInTransactionAsync(
            context,
            async (db, cancellationToken) => await EfCoreTransactionExecutor.ExecuteInTransactionAsync(
                db,
                (nestedDb, nestedCancellationToken) => Task.CompletedTask,
                isolationLevel: DataIsolationLevel.Serializable,
                cancellationToken: cancellationToken),
            isolationLevel: DataIsolationLevel.ReadCommitted));
    }

    [Fact]
    public async Task NestedCallCannotSpanDbContexts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await using var other = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => EfCoreTransactionExecutor.ExecuteInTransactionAsync(
            context,
            async (db, cancellationToken) => await EfCoreTransactionExecutor.ExecuteInTransactionAsync(
                other,
                (nestedDb, nestedCancellationToken) => Task.CompletedTask,
                cancellationToken: cancellationToken)));
    }

    [Fact]
    public async Task ExecutorRejectsExistingDbContextTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => EfCoreTransactionExecutor.ExecuteInTransactionAsync(
            context,
            (db, cancellationToken) => Task.CompletedTask));
    }

    [Fact]
    public async Task ExecutorRejectsAmbientTransactionScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        await Assert.ThrowsAsync<InvalidOperationException>(() => EfCoreTransactionExecutor.ExecuteInTransactionAsync(
            context,
            (db, cancellationToken) => Task.CompletedTask));
    }
}
