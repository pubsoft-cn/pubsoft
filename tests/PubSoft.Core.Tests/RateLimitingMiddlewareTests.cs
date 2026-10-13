using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RateLimitingMiddlewareTests
{
    [Fact]
    public async Task LimitsByNormalizedRemoteIpAndIgnoresForwardedHeaders()
    {
        var limiter = new RateLimiter(new RateLimiterOptions
        {
            RequestsPerWindow = 1,
            WindowSize = TimeSpan.FromMinutes(1)
        });
        var nextCount = 0;
        var middleware = new PubSoftRateLimitingMiddleware(
            _ =>
            {
                nextCount++;
                return Task.CompletedTask;
            },
            limiter);
        var first = CreateContext("::ffff:192.0.2.10", "198.51.100.20");
        var second = CreateContext("192.0.2.10", "203.0.113.30");

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);

        Assert.Equal(1, nextCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
        Assert.True(int.TryParse(second.Response.Headers.RetryAfter, out var retryAfter));
        Assert.True(retryAfter >= 1);
        Assert.Equal("0", second.Response.Headers["RateLimit-Remaining"]);
    }

    [Fact]
    public async Task MissingRemoteIpFailsClosed()
    {
        var middleware = new PubSoftRateLimitingMiddleware(
            _ => throw new InvalidOperationException("The request should have been rejected."),
            new RateLimiter());
        var context = CreateContext(null, "192.0.2.1");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }

    [Fact]
    public void RegistrationUsesConfiguredLimiter()
    {
        var services = new ServiceCollection();
        services.AddPubSoftRateLimiting(options => options.RequestsPerWindow = 7);
        using var provider = services.BuildServiceProvider();

        var limiter = provider.GetRequiredService<RateLimiter>();

        Assert.True(limiter.CheckByIpAddress("192.0.2.10").IsAllowed);
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPubSoftRateLimiting(
            options => options.LimitByUserId = true));
    }

    [Fact]
    public async Task FailureOnlyPolicyCountsCompletedFailuresForSelectedRoutes()
    {
        var policy = new PubSoftRateLimitPolicy(new PubSoftRateLimitPolicyOptions
        {
            Name = "login-failures",
            RequestsPerWindow = 1,
            CountFailuresOnly = true,
            AppliesTo = context => context.Request.Path.StartsWithSegments("/login"),
            IdentifierSelector = context => IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString())
        });
        var nextCount = 0;
        var middleware = new PubSoftRateLimitPolicyMiddleware(
            context =>
            {
                nextCount++;
                context.Response.StatusCode = int.Parse(context.Request.Query["status"].ToString());
                return Task.CompletedTask;
            },
            [policy]);

        var success = CreatePolicyContext("/login", "200");
        await middleware.InvokeAsync(success);
        await success.Response.CompleteAsync();

        var failure = CreatePolicyContext("/login", "401");
        await middleware.InvokeAsync(failure);
        await failure.Response.CompleteAsync();

        var unrelatedFailure = CreatePolicyContext("/health", "500");
        await middleware.InvokeAsync(unrelatedFailure);
        await unrelatedFailure.Response.CompleteAsync();

        var blocked = CreatePolicyContext("/login", "401");
        await middleware.InvokeAsync(blocked);

        Assert.Equal(3, nextCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public async Task NamedPoliciesStackWithIndependentIdentifiers()
    {
        var services = new ServiceCollection();
        services.AddPubSoftRateLimitPolicy(options =>
        {
            options.Name = "api-ip";
            options.RequestsPerWindow = 10;
            options.AppliesTo = context => context.Request.Path.StartsWithSegments("/api");
            options.IdentifierSelector = context => IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
        });
        services.AddPubSoftRateLimitPolicy(options =>
        {
            options.Name = "login-client";
            options.RequestsPerWindow = 1;
            options.AppliesTo = context => context.Request.Path.StartsWithSegments("/api/login");
            options.IdentifierSelector = context => context.Request.Query["client"].ToString();
        });
        Assert.Throws<ArgumentException>(() => services.AddPubSoftRateLimitPolicy(options => options.Name = "api-ip"));

        using var provider = services.BuildServiceProvider();
        var nextCount = 0;
        var middleware = new PubSoftRateLimitPolicyMiddleware(
            _ =>
            {
                nextCount++;
                return Task.CompletedTask;
            },
            provider.GetServices<PubSoftRateLimitPolicy>());

        await middleware.InvokeAsync(CreateClientPolicyContext("/api/login", "alpha"));
        await middleware.InvokeAsync(CreateClientPolicyContext("/api/login", "beta"));
        var blocked = CreateClientPolicyContext("/api/login", "alpha");
        await middleware.InvokeAsync(blocked);
        await middleware.InvokeAsync(CreateClientPolicyContext("/api/other", "alpha"));

        Assert.Equal(3, nextCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public async Task FailureOnlyPolicyCountsDownstreamExceptions()
    {
        var policy = new PubSoftRateLimitPolicy(new PubSoftRateLimitPolicyOptions
        {
            Name = "exception-failures",
            RequestsPerWindow = 1,
            CountFailuresOnly = true,
            IdentifierSelector = _ => "client"
        });
        var nextCount = 0;
        var middleware = new PubSoftRateLimitPolicyMiddleware(
            _ =>
            {
                nextCount++;
                throw new InvalidOperationException("request failed");
            },
            [policy]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
        var blocked = new DefaultHttpContext();
        await middleware.InvokeAsync(blocked);

        Assert.Equal(1, nextCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(string? remoteIp, string forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        return context;
    }

    private static DefaultHttpContext CreatePolicyContext(string path, string statusCode)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Path = path;
        context.Request.QueryString = new QueryString($"?status={statusCode}");
        return context;
    }

    private static DefaultHttpContext CreateClientPolicyContext(string path, string clientId)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Path = path;
        context.Request.QueryString = new QueryString($"?client={clientId}");
        return context;
    }
}