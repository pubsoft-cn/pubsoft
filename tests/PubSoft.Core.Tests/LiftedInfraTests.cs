using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.AspNetCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class LiftedInfraTests
{
    [Fact]
    public async Task GlobalExceptionHandler_MapsDomainExceptionTo400()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(ctx, new DomainException("bad state"), default);

        Assert.True(handled);
        Assert.Equal(400, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        Assert.Contains("\"code\":400", body);
        Assert.Contains("bad state", body);
        Assert.Contains("traceId", body);
    }

    [Fact]
    public async Task GlobalExceptionHandler_CustomMappingWinsOverDefaults()
    {
        var options = new GlobalExceptionHandlerOptions();
        options.CustomMappings[typeof(IOException)] = 503;
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, options);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(ctx, new IOException("io"), default);

        Assert.Equal(503, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task GlobalExceptionHandler_UnknownExceptionYields500()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(ctx, new Exception("boom"), default);

        Assert.Equal(500, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task DbFirstConfigProvider_FallbackHitsWhenDbEmpty_ThenCaches()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRepository<CfgRow, long>, EmptyRepo>();
        var provider = services.BuildServiceProvider();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var p = new TestConfigProvider(provider, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cfg:Items:0:Key"] = "k1",
                ["Cfg:Items:0:Val"] = "fallback-value",
            })
            .Build(), cache);

        var result = await p.GetConfigAsync("k1");

        Assert.NotNull(result);
        Assert.Equal("fallback-value", result!.Val);

        var cached = await p.GetConfigAsync("k1");
        Assert.Equal("fallback-value", cached!.Val);
    }

    [Fact]
    public async Task DbFirstConfigProvider_MissReturnsNull()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRepository<CfgRow, long>, EmptyRepo>();
        var provider = services.BuildServiceProvider();
        var p = new TestConfigProvider(provider, new ConfigurationBuilder().Build(),
            new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await p.GetConfigAsync("nope"));
    }

    [Fact]
    public void InvalidateCache_RemovesEntry()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var p = new TestConfigProvider(new ServiceCollection().BuildServiceProvider(),
            new ConfigurationBuilder().Build(), cache);
        cache.Set("pfx_x", new CfgInfo { Key = "x" });

        p.InvalidateCache("x");

        Assert.False(cache.TryGetValue("pfx_x", out _));
    }

    private sealed class CfgRow { public long Id { get; set; } public string Key { get; set; } = ""; public string Val { get; set; } = ""; }
    private sealed class CfgInfo { public string Key { get; set; } = ""; public string Val { get; set; } = ""; }

    private sealed class EmptyRepo : IRepository<CfgRow, long>
    {
        public Task<IQueryable<CfgRow>> GetQueryableAsync() => Task.FromResult(Enumerable.Empty<CfgRow>().AsQueryable());
        public Task<CfgRow> GetAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CfgRow?> FindAsync(long id, CancellationToken ct = default) => Task.FromResult<CfgRow?>(null);
        public Task<CfgRow> InsertAsync(CfgRow e, bool s = false, CancellationToken ct = default) => Task.FromResult(e);
        public Task<CfgRow> UpdateAsync(CfgRow e, bool s = false, CancellationToken ct = default) => Task.FromResult(e);
        public Task DeleteAsync(long id, bool s = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(CfgRow e, bool s = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateManyAsync(IEnumerable<CfgRow> es, bool s = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task InsertManyAsync(IEnumerable<CfgRow> es, bool s = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestConfigProvider : DbFirstConfigProvider<CfgRow, CfgInfo>
    {
        public TestConfigProvider(IServiceProvider sp, IConfiguration c, IMemoryCache m)
            : base(sp, c, m, NullLogger<TestConfigProvider>.Instance) { }
        protected override string CachePrefix => "pfx_";
        protected override string FallbackSectionName => "Cfg:Items";
        protected override string ConfigKey(CfgInfo info) => info.Key;
        protected override System.Linq.Expressions.Expression<Func<CfgRow, bool>> DbPredicate(string key) => r => r.Key == key;
        protected override CfgInfo ToInfo(CfgRow entity) => new() { Key = entity.Key, Val = entity.Val };
    }
}
