using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ForwardedHeadersExtensionsTests
{
    [Fact]
    public void AddPubSoftForwardedHeaders_TrustAnyProxy_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddPubSoftForwardedHeaders(o => o.TrustAnyProxy = true);
        using var provider = services.BuildServiceProvider();

        var fho = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.True(fho.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(fho.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.Empty(fho.KnownProxies);
        Assert.Empty(fho.KnownIPNetworks);
    }

    [Fact]
    public void AddPubSoftForwardedHeaders_Default_KeepsPlatformTrustDefaults()
    {
        var services = new ServiceCollection();
        services.AddPubSoftForwardedHeaders();
        using var provider = services.BuildServiceProvider();

        var fho = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        // 默认不信任任意上游 — 保留平台默认（loopback）而非清空白名单
        Assert.NotEmpty(fho.KnownProxies);
    }

    [Fact]
    public async Task UsePubSoftForwardedHeaders_RewritesRemoteIpAddress()
    {
        var captured = string.Empty;
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UsePubSoftForwardedHeaders(o => o.TrustAnyProxy = true);
        app.Run(ctx =>
        {
            captured = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.55";

        await pipeline(context);

        Assert.Equal("203.0.113.55", captured);
        Assert.Equal("203.0.113.55", context.Connection.RemoteIpAddress?.ToString());
    }

    [Fact]
    public async Task UsePubSoftForwardedHeaders_UntrustedPeer_IgnoresHeader()
    {
        var captured = string.Empty;
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        // 默认不信任任意上游：peer 10.0.0.1 非 loopback → XFF 不被采纳
        app.UsePubSoftForwardedHeaders();
        app.Run(ctx =>
        {
            captured = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.55";

        await pipeline(context);

        Assert.Equal("10.0.0.1", captured);
    }

    [Fact]
    public async Task UsePubSoftForwardedHeaders_RealIp_RewritesForTrustedPeer()
    {
        var captured = string.Empty;
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UsePubSoftForwardedHeaders(o =>
        {
            o.RealIp = true;
            o.KnownNetworks.Add("10.0.0.0/8");
        });
        app.Run(ctx =>
        {
            captured = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Real-IP"] = "203.0.113.9";

        await pipeline(context);

        Assert.Equal("203.0.113.9", captured);
    }

    [Fact]
    public async Task UsePubSoftForwardedHeaders_RealIp_DefaultOff_IgnoresHeader()
    {
        // RealIp 默认关闭：即使 peer 可信（loopback），X-Real-IP 也不被采纳
        var captured = string.Empty;
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UsePubSoftForwardedHeaders();
        app.Run(ctx =>
        {
            captured = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Real-IP"] = "203.0.113.9";

        await pipeline(context);

        Assert.Equal("127.0.0.1", captured);
    }

    [Fact]
    public async Task UsePubSoftForwardedHeaders_RealIp_YieldsToXForwardedFor()
    {
        // XFF 在场时 X-Real-IP 不生效——信任链结论优先
        var captured = string.Empty;
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UsePubSoftForwardedHeaders(o =>
        {
            o.RealIp = true;
            o.TrustAnyProxy = true;
        });
        app.Run(ctx =>
        {
            captured = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.55";
        context.Request.Headers["X-Real-IP"] = "203.0.113.9";

        await pipeline(context);

        Assert.Equal("203.0.113.55", captured);
    }

    [Fact]
    public async Task UsePubSoftForwardedHeaders_RealIp_UntrustedPeer_IgnoresHeader()
    {
        var captured = string.Empty;
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());
        // peer 10.0.0.1 不在默认 loopback 白名单 → X-Real-IP 不被采纳
        app.UsePubSoftForwardedHeaders(o => o.RealIp = true);
        app.Run(ctx =>
        {
            captured = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Real-IP"] = "203.0.113.9";

        await pipeline(context);

        Assert.Equal("10.0.0.1", captured);
    }

    [Fact]
    public void AddPubSoftForwardedHeaders_InvalidTrustEntries_Throw()
    {
        // 非法白名单条目启动期抛错而非静默丢弃
        Assert.Throws<ArgumentException>(() => new ServiceCollection()
            .AddPubSoftForwardedHeaders(o => o.KnownProxies.Add("not-an-ip")));
        Assert.Throws<ArgumentException>(() => new ServiceCollection()
            .AddPubSoftForwardedHeaders(o => o.KnownNetworks.Add("10.0.0.0")));
    }

    [Fact]
    public void AddPubSoftForwardedHeaders_RealIp_Throws()
    {
        // 平台 ForwardedHeadersOptions 表达不了 X-Real-IP——拒绝静默失配
        Assert.Throws<ArgumentException>(() => new ServiceCollection()
            .AddPubSoftForwardedHeaders(o => o.RealIp = true));
    }
}
