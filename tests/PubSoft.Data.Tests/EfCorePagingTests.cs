using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EfCorePagingTests : IDisposable
{
    private sealed class E : Entity<long> { public int N { get; set; } }
    private sealed class Tenant : ITenantContext { public long? TenantId => null; }
    private sealed class Ctx(DbContextOptions<Ctx> o) : PubSoftDbContext(o, new Tenant())
    {
        public DbSet<E> Items => Set<E>();
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly Ctx _db;

    public EfCorePagingTests()
    {
        _conn.Open();
        _db = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        for (var i = 1; i <= 25; i++)
            _db.Items.Add(new E { N = i });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private IQueryable<E> Ordered() => _db.Items.OrderBy(e => e.N);

    [Fact]
    public async Task FirstPageReturnsTotalAndItems()
    {
        var result = await Ordered().ToPagedResultAsync(1, 10);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(1, result.Items[0].N);
    }

    [Fact]
    public async Task LastPageReturnsRemainder()
    {
        var result = await Ordered().ToPagedResultAsync(3, 10);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(21, result.Items[0].N);
    }

    [Fact]
    public async Task PageIndexBelowOneClampsToFirstPage()
    {
        var result = await Ordered().ToPagedResultAsync(0, 10);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(1, result.Items[0].N);
    }

    [Fact]
    public async Task PageSizeAboveMaxClampsToMax()
    {
        var result = await Ordered().ToPagedResultAsync(1, 500);
        Assert.Equal(25, result.Items.Count);
    }

    [Fact]
    public async Task PageSizeBelowOneClampsToOne()
    {
        var result = await Ordered().ToPagedResultAsync(1, 0);
        Assert.Single(result.Items);
        Assert.Equal(25, result.TotalCount);
    }

    [Fact]
    public async Task CustomMaxPageSizeCaps()
    {
        var result = await Ordered().ToPagedResultAsync(1, 50, maxPageSize: 7);
        Assert.Equal(7, result.Items.Count);
    }

    [Fact]
    public async Task PageBeyondRangeReturnsEmptyWithTotal()
    {
        var result = await Ordered().ToPagedResultAsync(9, 10);
        Assert.Equal(25, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task NonPositiveMaxPageSizeThrows()
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Ordered().ToPagedResultAsync(1, 10, maxPageSize: 0));

    [Fact]
    public async Task OffsetBeyondIntRangeReturnsEmptyWithTotal()
    {
        var result = await Ordered().ToPagedResultAsync(int.MaxValue, 200);
        Assert.Equal(25, result.TotalCount);
        Assert.Empty(result.Items);
    }
}
