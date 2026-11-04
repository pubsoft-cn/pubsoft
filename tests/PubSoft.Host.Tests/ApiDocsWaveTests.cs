using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using PubSoft.AspNetCore.Docs;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ApiDocsWaveTests
{
    [Fact]
    public void ApiDocsOptionsDefaults()
    {
        var o = new ApiDocsOptions();
        Assert.Equal("/docs/catalog", o.CatalogPath);
        Assert.Equal("/docs", o.PagePath);
        Assert.Equal("API Docs", o.Title);
        Assert.True(o.DevelopmentOnly);
        Assert.True(o.DetectAuthMetadata);
        Assert.False(o.RequireAuthorization);
        Assert.False(o.IncludeNonActionEndpoints);
        Assert.Empty(o.ExtraEndpoints);
    }

    [Fact]
    public void ApiDocExtraEndpointFields()
    {
        var e = new ApiDocExtraEndpoint { Method = "POST", Path = "/spi/x", Summary = "s", Auth = "none" };
        Assert.Equal("POST", e.Method);
        Assert.Equal("/spi/x", e.Path);
        Assert.Equal("none", e.Auth);
        Assert.Equal("WS", new ApiDocExtraEndpoint().Method); // default
    }

    [Fact]
    public void CatalogServiceReturnsCatalogShape()
    {
        var svc = new ApiDocsCatalogService(
            new EmptyEndpointDataSource(), new FakeEnv(),
            Options.Create(new ApiDocsOptions()));
        var catalog = svc.GetCatalog();
        Assert.NotNull(catalog);
    }

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class EmptyEndpointDataSource : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => [];
        public override IChangeToken GetChangeToken() => NullChangeToken.Singleton;
    }
}
