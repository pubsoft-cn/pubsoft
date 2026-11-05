using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class GatewayRateLimitingWaveTests
{
    [Fact]
    public void RegistersAggregatePerIpPolicies()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var names = provider.GetServices<PubSoftRateLimitPolicy>().Select(p => p.Name).ToArray();

        Assert.Contains("validate-ip", names);
        Assert.Contains("validate-client", names);
        Assert.Contains("validate-anon", names);
        Assert.Contains("oauth-ip", names);
        Assert.Contains("oauth", names);
    }

    [Fact]
    public async Task ForgedClientIdsCollapseIntoAnonymousBucket()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Gateway:RateLimit:ValidateAnonymousPermitLimit"] = "1",
            ["Gateway:RateLimit:ValidatePermitLimit"] = "100",
            ["Gateway:RateLimit:ValidatePerIpPermitLimit"] = "100",
            ["Gateway:RateLimit:MaxClientIdsPerIp"] = "2",
        });
        var nextCount = 0;
        var middleware = Middleware(provider, () => nextCount++);

        // Two distinct client IDs fit the cardinality budget — separate partitions.
        await middleware.InvokeAsync(ValidateContext(clientId: "c1"));
        await middleware.InvokeAsync(ValidateContext(clientId: "c2"));
        Assert.Equal(2, nextCount);

        // The third forged identity cannot claim a client partition: it falls back
        // to the anonymous per-IP bucket (limit 1), so it and c4 share that bucket.
        await middleware.InvokeAsync(ValidateContext(clientId: "c3"));
        var blocked = ValidateContext(clientId: "c4");
        await middleware.InvokeAsync(blocked);

        Assert.Equal(3, nextCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public async Task DistinctClientIdsWithinCapKeepSeparateBuckets()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Gateway:RateLimit:ValidateAnonymousPermitLimit"] = "100",
            ["Gateway:RateLimit:ValidatePermitLimit"] = "1",
            ["Gateway:RateLimit:ValidatePerIpPermitLimit"] = "100",
            ["Gateway:RateLimit:MaxClientIdsPerIp"] = "2",
        });
        var middleware = Middleware(provider, () => { });

        await middleware.InvokeAsync(ValidateContext(clientId: "c1"));
        var sameClientAgain = ValidateContext(clientId: "c1");
        await middleware.InvokeAsync(sameClientAgain);
        var otherClient = ValidateContext(clientId: "c2");
        await middleware.InvokeAsync(otherClient);

        // c1 exhausted its own partition (limit 1); c2's partition is independent.
        Assert.Equal(StatusCodes.Status429TooManyRequests, sameClientAgain.Response.StatusCode);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, otherClient.Response.StatusCode);
    }

    [Fact]
    public async Task PerIpAggregateCapBoundsClientIdRotation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Gateway:RateLimit:ValidateAnonymousPermitLimit"] = "100",
            ["Gateway:RateLimit:ValidatePermitLimit"] = "100",
            ["Gateway:RateLimit:ValidatePerIpPermitLimit"] = "2",
            ["Gateway:RateLimit:MaxClientIdsPerIp"] = "100",
        });
        var middleware = Middleware(provider, () => { });

        await middleware.InvokeAsync(ValidateContext(clientId: "c1"));
        await middleware.InvokeAsync(ValidateContext(clientId: "c2"));
        // Even with fresh client IDs under the cardinality cap, the unconditional
        // per-IP aggregate policy bounds total requests from one address.
        var blocked = ValidateContext(clientId: "c3");
        await middleware.InvokeAsync(blocked);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public void MalformedTrustedProxyFailsAtRegistration()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Gateway:RateLimit:TrustedProxies:0"] = "not-an-ip",
        });
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddPubSoftGatewayRateLimiting(config));
    }

    [Fact]
    public void TrustedProxyListsPopulateForwardedHeadersOptions()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Gateway:RateLimit:TrustedProxies:0"] = "10.1.2.3",
            ["Gateway:RateLimit:TrustedNetworks:0"] = "172.16.0.0/12",
        });

        var fho = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Contains(fho.KnownProxies, ip => ip.Equals(IPAddress.Parse("10.1.2.3")));
        Assert.Contains(fho.KnownIPNetworks, n => n.Contains(IPAddress.Parse("172.18.5.4")));
    }

    [Fact]
    public void EmptyTrustListClearsKnownProxies()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var fho = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Empty(fho.KnownProxies);
        Assert.Empty(fho.KnownIPNetworks);
    }

    [Fact]
    public void CardinalityGuardRefusesIdsBeyondCap()
    {
        var guard = new ClientIdCardinalityGuard(2, 10, TimeSpan.FromMinutes(5));

        Assert.True(guard.Admit("ip", "a"));
        Assert.True(guard.Admit("ip", "b"));
        Assert.False(guard.Admit("ip", "c"));
        // Seen identities keep passing; refusals do not consume the budget.
        Assert.True(guard.Admit("ip", "a"));
        Assert.False(guard.Admit("ip", "d"));
        // Other IPs have their own budget.
        Assert.True(guard.Admit("ip2", "c"));
    }

    [Fact]
    public void CardinalityGuardResetsAfterWindow()
    {
        var time = new AdvancingTimeProvider();
        var guard = new ClientIdCardinalityGuard(1, 10, TimeSpan.FromMinutes(1), time);

        Assert.True(guard.Admit("ip", "a"));
        Assert.False(guard.Admit("ip", "b"));

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.True(guard.Admit("ip", "b"));
    }

    [Fact]
    public void CardinalityGuardBoundsTrackedIps()
    {
        var guard = new ClientIdCardinalityGuard(1, 1, TimeSpan.FromMinutes(5));

        Assert.True(guard.Admit("ip1", "a"));
        Assert.False(guard.Admit("ip2", "a"));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddPubSoftGatewayRateLimiting(BuildConfig(values));
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static PubSoftRateLimitPolicyMiddleware Middleware(
        ServiceProvider provider, Action onNext)
        => new(_ =>
            {
                onNext();
                return Task.CompletedTask;
            },
            provider.GetServices<PubSoftRateLimitPolicy>());

    private static DefaultHttpContext ValidateContext(string? clientId)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Path = "/api/auth/validate";
        if (clientId is not null)
            context.Request.Headers["X-Client-Id"] = clientId;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan delta) => _now += delta;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
