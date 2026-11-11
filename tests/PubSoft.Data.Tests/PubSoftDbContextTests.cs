using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class PubSoftDbContextTests
{
    [Fact]
    public async Task ScopeFiltersReadCurrentContextValuesDespiteModelCaching()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var firstScope = CreateContext(options, tenantId: 1, memberId: null, isMemberScoped: false))
        {
            await firstScope.Database.EnsureCreatedAsync();
            firstScope.Records.AddRange(
                new ScopeRecord { Id = 1, MemberId = 10 },
                new ScopeRecord { Id = 2, MemberId = 20 });
            await firstScope.SaveChangesAsync();
            Assert.Equal(2, await firstScope.Records.CountAsync());
        }

        await using (var anotherMember = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true))
            Assert.Equal(1, await anotherMember.Records.CountAsync());

        await using (var differentMember = CreateContext(options, tenantId: 1, memberId: 20, isMemberScoped: true))
            Assert.Equal(1, await differentMember.Records.CountAsync());

        await using (var anotherTenant = CreateContext(options, tenantId: 2, memberId: 20, isMemberScoped: true))
        {
            anotherTenant.Records.Add(new ScopeRecord { Id = 3, MemberId = 20 });
            await anotherTenant.SaveChangesAsync();
            Assert.Equal(1, await anotherTenant.Records.CountAsync());
        }
    }

    [Fact]
    public async Task ScopeBypassPreservesSoftDeleteAndDeleteIsConvertedToUpdate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));

        await using (var seed = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true, clock))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Records.Add(new ScopeRecord { Id = 1, MemberId = 10 });
            await seed.SaveChangesAsync();

            seed.Records.Remove(await seed.Records.SingleAsync(record => record.Id == 1));
            seed.SaveChanges();
        }

        // Foreign-member fixture rows are written under that member's own scope —
        // the write guard rejects explicit foreign MemberIds on Added entities.
        await using (var foreign = CreateContext(options, tenantId: 1, memberId: 20, isMemberScoped: true, clock))
        {
            foreign.Records.Add(new ScopeRecord { Id = 2, MemberId = 20 });
            await foreign.SaveChangesAsync();
        }

        await using var scoped = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true, clock);
        Assert.Empty(await scoped.Records.ToListAsync());

        var acrossScopes = await scoped.Records.IgnoreScopeFilters().ToListAsync();
        var deleted = Assert.Single(acrossScopes);
        Assert.Equal(2, deleted.Id);

        var includingDeleted = await scoped.Records.IgnoreQueryFilters().ToListAsync();
        var softDeleted = Assert.Single(includingDeleted, record => record.Id == 1);
        Assert.True(softDeleted.IsDeleted);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, softDeleted.DeletionTime);
    }

    [Fact]
    public async Task RepositoryFindHonorsCurrentScopeForPreviouslyTrackedEntities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var tenantOne = CreateContext(options, tenantId: 1, memberId: null, isMemberScoped: false))
        {
            await tenantOne.Database.EnsureCreatedAsync();
            tenantOne.Records.AddRange(
                new ScopeRecord { Id = 1, MemberId = 10 },
                new ScopeRecord { Id = 2, MemberId = 20 });
            await tenantOne.SaveChangesAsync();
        }

        await using (var tenantTwo = CreateContext(options, tenantId: 2, memberId: null, isMemberScoped: false))
        {
            tenantTwo.Records.Add(new ScopeRecord { Id = 3, MemberId = 10 });
            await tenantTwo.SaveChangesAsync();
        }

        await using var scoped = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true);
        var repository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(scoped);
        Assert.Equal(1, (await repository.FindAsync(1))?.Id);

        await scoped.Records.IgnoreScopeFilters().SingleAsync(record => record.Id == 2);
        await scoped.Records.IgnoreQueryFilters().SingleAsync(record => record.Id == 3);

        Assert.Null(await repository.FindAsync(2));
        Assert.Null(await repository.FindAsync(3));
        await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.InsertManyAsync(new[] {
                    new ScopeRecord { Id = 4, TenantId = 1, MemberId = 10 },
                    new ScopeRecord { Id = 5, TenantId = 2, MemberId = 10 }
                }));
        Assert.DoesNotContain(scoped.ChangeTracker.Entries<ScopeRecord>(), entry => entry.Entity.Id == 4);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpdateAsync(new ScopeRecord { Id = 4, TenantId = 2, MemberId = 10 }));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.DeleteAsync(new ScopeRecord { Id = 4, TenantId = 2, MemberId = 10 }));

        await using var hostScope = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        var hostRepository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(hostScope);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hostRepository.UpdateAsync(new ScopeRecord { Id = 4, TenantId = 1, MemberId = 10 }));
    }

    [Fact]
    public async Task MissingMemberIdFailsClosedWhenMemberScopeIsEnabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var setup = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Records.Add(new ScopeRecord { Id = 1, MemberId = 10 });
            await setup.SaveChangesAsync();
        }

        await using var invalidScope = CreateContext(options, tenantId: 1, memberId: null, isMemberScoped: true);
        Assert.Empty(await invalidScope.Records.ToListAsync());

        await invalidScope.Records.IgnoreScopeFilters().SingleAsync(record => record.Id == 1);
        var repository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(invalidScope);
        Assert.Null(await repository.FindAsync(1));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.InsertAsync(new ScopeRecord { Id = 2, TenantId = 1, MemberId = 10 }));
    }

    [Fact]
    public async Task RepositoryWritesCannotUseForgedScopeValuesToTargetAnotherRowsKey()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var setup = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Records.Add(new ScopeRecord { Id = 1, MemberId = 10, Value = "tenant-one" });
            await setup.SaveChangesAsync();
        }

        await using (var otherTenant = CreateContext(options, tenantId: 2, memberId: 20, isMemberScoped: true))
        {
            otherTenant.Records.Add(new ScopeRecord { Id = 2, MemberId = 20, Value = "tenant-two" });
            await otherTenant.SaveChangesAsync();
        }

        await using (var scoped = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true))
        {
            var repository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(scoped);
            var forgedForeignRow = new ScopeRecord { Id = 2, TenantId = 1, MemberId = 10, Value = "tampered" };

            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.UpdateAsync(forgedForeignRow));
            await repository.DeleteAsync(forgedForeignRow);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.UpdateManyAsync(new[]
            {
                new ScopeRecord { Id = 1, TenantId = 1, MemberId = 10, Value = "partially-updated" },
                forgedForeignRow
            }));

            Assert.Equal("tenant-one", (await repository.GetAsync(1)).Value);
            Assert.DoesNotContain(scoped.ChangeTracker.Entries<ScopeRecord>(), entry => entry.State == EntityState.Deleted);
            await scoped.SaveChangesAsync();
        }

        await using var verification = CreateContext(options, tenantId: 2, memberId: 20, isMemberScoped: true);
        Assert.Equal("tenant-two", (await verification.Records.SingleAsync()).Value);
    }

    [Fact]
    public async Task StaleScopedUpdateCannotWriteAfterTenantOrMemberScopeChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var setup = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Records.Add(new ScopeRecord { Id = 1, MemberId = 10, Value = "initial" });
            await setup.SaveChangesAsync();
        }

        await using var staleScope = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true);
        var staleRecord = await staleScope.Records.SingleAsync();
        staleRecord.Value = "stale-write";

        await using (var reassignment = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false))
        {
            var currentRecord = await reassignment.Records.IgnoreQueryFilters().SingleAsync(record => record.Id == 1);
            currentRecord.TenantId = 2;
            currentRecord.MemberId = 20;
            await reassignment.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleScope.SaveChangesAsync());

        await using var verify = CreateContext(options, tenantId: 2, memberId: 20, isMemberScoped: true);
        var reassignedRecord = await verify.Records.SingleAsync();
        Assert.Equal("initial", reassignedRecord.Value);
        Assert.Equal(2, reassignedRecord.TenantId);
        Assert.Equal(20, reassignedRecord.MemberId);
    }

    [Fact]
    public async Task StaleScopedDeleteCannotDeleteAfterTenantOrMemberScopeChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var setup = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Records.Add(new ScopeRecord { Id = 1, MemberId = 10, Value = "preserve" });
            await setup.SaveChangesAsync();
        }

        await using var staleScope = CreateContext(options, tenantId: 1, memberId: 10, isMemberScoped: true);
        var staleRecord = await staleScope.Records.SingleAsync();
        staleScope.Records.Remove(staleRecord);

        await using (var reassignment = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false))
        {
            var currentRecord = await reassignment.Records.IgnoreQueryFilters().SingleAsync(record => record.Id == 1);
            currentRecord.TenantId = 2;
            currentRecord.MemberId = 20;
            await reassignment.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleScope.SaveChangesAsync());

        await using var verify = CreateContext(options, tenantId: 2, memberId: 20, isMemberScoped: true);
        var reassignedRecord = await verify.Records.SingleAsync();
        Assert.Equal("preserve", reassignedRecord.Value);
        Assert.False(reassignedRecord.IsDeleted);
    }

    [Fact]
    public async Task ScopedRegistrationsShareRepositoryAndUnitOfWorkContext()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ScopeDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<ITenantContext>(_ => new TenantContext(1));
        services.AddScoped<IMemberScopeContext>(_ => new MemberScopeContext(10, true));
        services.AddPubSoftRepository<ScopeRecord, int, ScopeDbContext>();
        services.AddPubSoftUnitOfWork<ScopeDbContext>();
        services.AddPubSoftSaveChangesHook<ScopeDbContext, RecordingSaveChangesHook>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scopedServices = scope.ServiceProvider;
        var repository = scopedServices.GetRequiredService<IRepository<ScopeRecord, int>>();
        var readOnlyRepository = scopedServices.GetRequiredService<IReadOnlyRepository<ScopeRecord, int>>();
        var efRepository = scopedServices.GetRequiredService<IEfCoreRepository<ScopeRecord, int>>();
        var unitOfWork = scopedServices.GetRequiredService<IPubSoftUnitOfWork<ScopeDbContext>>();

        Assert.Same(repository, readOnlyRepository);
        Assert.Same(repository, efRepository);
        Assert.Same(unitOfWork.DbContext, scopedServices.GetRequiredService<ScopeDbContext>());

        await unitOfWork.DbContext.Database.EnsureCreatedAsync();
        await repository.InsertAsync(new ScopeRecord { Id = 1, MemberId = 10 });
        // No activated save boundary in this scope — the insert persisted immediately,
        // and the save hook already observed the Added entry at that save.
        Assert.Equal(EntityState.Added, scopedServices.GetRequiredService<ISaveChangesHook>() is RecordingSaveChangesHook hook
            ? hook.LastState
            : null);
        Assert.Equal(0, await unitOfWork.SaveChangesAsync());
        Assert.Equal(1, (await repository.FindAsync(1))?.Id);
    }

    [Fact]
    public async Task SaveChangesHookSeesDeleteIntentBeforeSoftDeleteConversion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var hook = new RecordingSaveChangesHook();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var setup = CreateContext(options, 1, 10, true))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Records.Add(new ScopeRecord { Id = 1, MemberId = 10 });
            await setup.SaveChangesAsync();
        }

        await using (var context = CreateContext(options, 1, 10, true, hook: hook))
        {
            context.Records.Remove(await context.Records.SingleAsync());
            context.SaveChanges();
        }

        Assert.Equal(EntityState.Deleted, hook.LastState);
        await using var verify = CreateContext(options, 1, null, false);
        var deleted = await verify.Records.IgnoreQueryFilters().SingleAsync();
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task SaveChangesHookFailureAbortsDatabaseWrite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using (var setup = CreateContext(options, 1, 10, true))
            await setup.Database.EnsureCreatedAsync();

        await using (var context = CreateContext(options, 1, 10, true, hook: new RecordingSaveChangesHook(throwOnInvoke: true)))
        {
            context.Records.Add(new ScopeRecord { Id = 1, MemberId = 10 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using var verify = CreateContext(options, 1, null, false);
        Assert.Empty(await verify.Records.ToListAsync());
    }

    [Fact]
    public async Task InsertWithoutBoundaryPersistsImmediately()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using var context = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(context);

        await repository.InsertAsync(new ScopeRecord { Id = 1, Value = "no-boundary" });

        await using var verify = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        Assert.Single(await verify.Records.Where(r => r.Value == "no-boundary").ToListAsync());
    }

    [Fact]
    public async Task InsertWithActivatedBoundaryStagesUntilSaveAll()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using var context = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        await context.Database.EnsureCreatedAsync();
        var boundary = new TestSaveChangesBoundary();
        var repository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(context, boundary);

        boundary.Activate();
        boundary.RegisterServiceParticipants(new[] { context });
        await repository.InsertAsync(new ScopeRecord { Id = 2, Value = "staged" });

        await using (var verify = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false))
            Assert.Empty(await verify.Records.IgnoreQueryFilters().Where(r => r.Value == "staged").ToListAsync());

        await boundary.SaveAllAsync();

        await using var verifyAfter = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        Assert.Single(await verifyAfter.Records.Where(r => r.Value == "staged").ToListAsync());
    }

    [Fact]
    public async Task InactiveBoundaryStillPersistsImmediately()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using var context = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        await context.Database.EnsureCreatedAsync();
        // Boundary exists in DI but was never activated (middleware absent / worker scope).
        var repository = new EfCoreRepository<ScopeRecord, int, ScopeDbContext>(context, new TestSaveChangesBoundary());

        await repository.InsertAsync(new ScopeRecord { Id = 3, Value = "inactive-boundary" });

        await using var verify = CreateContext(options, tenantId: null, memberId: null, isMemberScoped: false);
        Assert.Single(await verify.Records.Where(r => r.Value == "inactive-boundary").ToListAsync());
    }

    private sealed class TestSaveChangesBoundary : ISaveChangesBoundary
    {
        private readonly List<Func<CancellationToken, Task>> _callbacks = new();
        public bool IsActive { get; private set; }
        public void Activate() => IsActive = true;
        public void Register(Func<CancellationToken, Task> saveCallback) => _callbacks.Add(saveCallback);
        public void RegisterServiceParticipants(IEnumerable<IPubSoftSaveParticipant> participants)
        {
            foreach (var participant in participants)
                _callbacks.Add(participant.SaveChangesAsync);
        }
        public async Task SaveAllAsync(CancellationToken cancellationToken = default)
        {
            foreach (var callback in _callbacks)
                await callback(cancellationToken);
            _callbacks.Clear();
        }
    }


    // R1: under member scope, deleting a null-MemberId record must be rejected —
    // attach+delete bypasses the query filter the same way as foreign ids.
    [Fact]
    public async Task MemberScopeCannotDeleteNullMemberRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScopeDbContext>().UseSqlite(connection).Options;

        await using var scoped = CreateContext(options, tenantId: null, memberId: 10, isMemberScoped: true);
        await scoped.Database.EnsureCreatedAsync();
        var orphan = new ScopeRecord { Id = 99, MemberId = null };
        scoped.Records.Attach(orphan);
        scoped.Records.Remove(orphan);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.SaveChangesAsync());
        Assert.Contains("outside the current member scope", ex.Message);
    }

    private static ScopeDbContext CreateContext(
        DbContextOptions<ScopeDbContext> options,
        long? tenantId,
        long? memberId,
        bool isMemberScoped,
        TimeProvider? timeProvider = null,
        ISaveChangesHook? hook = null)
        => new(options, new TenantContext(tenantId), new MemberScopeContext(memberId, isMemberScoped), timeProvider, hook);

    private sealed class ScopeDbContext(
        DbContextOptions<ScopeDbContext> options,
        ITenantContext tenantContext,
        IMemberScopeContext memberScopeContext,
        TimeProvider? timeProvider = null,
        ISaveChangesHook? saveChangesHook = null)
        : PubSoftDbContext(options, tenantContext, memberScopeContext, timeProvider, saveChangesHook)
    {
        public DbSet<ScopeRecord> Records => Set<ScopeRecord>();
    }

    private sealed class ScopeRecord : IMultiTenant, IMemberScoped, ISoftDelete
    {
        public int Id { get; set; }
        public long? TenantId { get; set; }
        public long? MemberId { get; set; }
        public string Value { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public long? DeleterId { get; set; }
        public DateTime? DeletionTime { get; set; }
    }

    private sealed record TenantContext(long? TenantId) : ITenantContext;

    private sealed record MemberScopeContext(long? MemberId, bool IsMemberScoped) : IMemberScopeContext;

    private sealed class RecordingSaveChangesHook(bool throwOnInvoke = false) : ISaveChangesHook
    {
        public EntityState? LastState { get; private set; }

        public void BeforeSaveChanges(DbContext context)
        {
            LastState = context.ChangeTracker.Entries<ScopeRecord>().FirstOrDefault()?.State;
            if (throwOnInvoke)
                throw new InvalidOperationException("Save hook rejected the change set.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}