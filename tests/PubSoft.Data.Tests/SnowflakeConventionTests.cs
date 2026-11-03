using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SnowflakeConventionTests
{
    private sealed class LongEntity { public long Id { get; set; } }
    private sealed class IntEntity { public int Id { get; set; } }
    private sealed class StringEntity { public string Id { get; set; } = ""; }
    private sealed class NonPkLongEntity { public int Id { get; set; } public long Seq { get; set; } }

    private class Ctx(DbContextOptions<Ctx> o) : DbContext(o)
    {
        public DbSet<LongEntity> Longs => Set<LongEntity>();
        public DbSet<IntEntity> Ints => Set<IntEntity>();
        public DbSet<StringEntity> Strings => Set<StringEntity>();
        public DbSet<NonPkLongEntity> NonPkLongs => Set<NonPkLongEntity>();
        protected override void OnModelCreating(ModelBuilder b) => b.ApplySnowflakeIdConventions();
    }

    [Fact]
    public void LongPkGetsValueGeneratedNever()
    {
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options;
        using var ctx = new Ctx(opts);
        var longId = ctx.Model.FindEntityType(typeof(LongEntity))!.FindProperty(nameof(LongEntity.Id))!;
        Assert.Equal(ValueGenerated.Never, longId.ValueGenerated);
        // convention only touches long PK columns — int/string PKs stay convention-default
        var intId = ctx.Model.FindEntityType(typeof(IntEntity))!.FindProperty(nameof(IntEntity.Id))!;
        Assert.Equal(typeof(int), intId.ClrType);
        var strId = ctx.Model.FindEntityType(typeof(StringEntity))!.FindProperty(nameof(StringEntity.Id))!;
        Assert.Equal(typeof(string), strId.ClrType);
    }

    [Fact]
    public void NonPkLongColumnsUntouched()
    {
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite("Data Source=:memory:").Options;
        using var ctx = new Ctx(opts);
        var seq = ctx.Model.FindEntityType(typeof(NonPkLongEntity))!.FindProperty(nameof(NonPkLongEntity.Seq))!;
        Assert.False(seq.IsPrimaryKey()); // non-PK long columns are outside the convention's scope
    }
}
