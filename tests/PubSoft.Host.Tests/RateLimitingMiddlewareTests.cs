using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task RejectionUsesRegisteredBodyWriter()
    {
        var services = new ServiceCollection();
        services.AddPubSoftRateLimitRejection(context =>
            context.Response.WriteAsJsonAsync(new { code = 429, message = "slow down" }));
        using var provider = services.BuildServiceProvider();

        var limiter = new RateLimiter(new RateLimiterOptions
        {
            RequestsPerWindow = 1,
            WindowSize = TimeSpan.FromMinutes(1)
        });
        var middleware = new PubSoftRateLimitingMiddleware(_ => Task.CompletedTask, limiter);

        var first = CreateContext("192.0.2.10", "198.51.100.20");
        first.RequestServices = provider;
        await middleware.InvokeAsync(first);

        var blocked = CreateContext("192.0.2.10", "198.51.100.21");
        blocked.RequestServices = provider;
        blocked.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(blocked);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        blocked.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(blocked.Response.Body).ReadToEndAsync();
        Assert.Contains("\"code\":429", body);
        Assert.DoesNotContain("rate_limit_exceeded", body);
    }

    [Fact]
    public async Task PolicyRejectionUsesRegisteredBodyWriter()
    {
        var services = new ServiceCollection();
        services.AddPubSoftRateLimitRejection(context =>
            context.Response.WriteAsJsonAsync(new { code = 429, message = "slow down" }));
        using var provider = services.BuildServiceProvider();

        var policy = new PubSoftRateLimitPolicy(new PubSoftRateLimitPolicyOptions
        {
            Name = "api-ip",
            RequestsPerWindow = 1,
            IdentifierSelector = context => IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString())
        });
        var middleware = new PubSoftRateLimitPolicyMiddleware(_ => Task.CompletedTask, [policy]);

        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        first.RequestServices = provider;
        await middleware.InvokeAsync(first);

        var blocked = new DefaultHttpContext();
        blocked.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        blocked.RequestServices = provider;
        blocked.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(blocked);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        blocked.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(blocked.Response.Body).ReadToEndAsync();
        Assert.Contains("\"code\":429", body);
        Assert.DoesNotContain("rate_limit_exceeded", body);
    }

    [Fact]
    public async Task FailureOnlyPolicyPassesHttpContextToIsFailureResponse()
    {
        HttpContext? capturedContext = null;
        var capturedStatus = 0;
        var policy = new PubSoftRateLimitPolicy(new PubSoftRateLimitPolicyOptions
        {
            Name = "credential-failures",
            RequestsPerWindow = 1,
            CountFailuresOnly = true,
            // 只统计带凭证请求的 401 —— 匿名探测不计数
            IsFailureResponse = (context, statusCode) =>
            {
                capturedContext = context;
                capturedStatus = statusCode;
                return statusCode == 401 && context.Request.Headers.ContainsKey("Authorization");
            }
        });
        var nextCount = 0;
        var middleware = new PubSoftRateLimitPolicyMiddleware(
            context =>
            {
                nextCount++;
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            },
            [policy]);

        var anonymous = new DefaultHttpContext();
        anonymous.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        await middleware.InvokeAsync(anonymous);

        var authenticated = new DefaultHttpContext();
        authenticated.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        authenticated.Request.Headers["Authorization"] = "Bearer token";
        await middleware.InvokeAsync(authenticated);

        var blocked = new DefaultHttpContext();
        blocked.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        blocked.Request.Headers["Authorization"] = "Bearer token";
        await middleware.InvokeAsync(blocked);

        Assert.Equal(2, nextCount);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        Assert.Same(authenticated, capturedContext);
        Assert.Equal(401, capturedStatus);
    }

    [Fact]
    public async Task WarnsOnceWhenForwardedHeadersUnprocessed()
    {
        var logger = new CapturingLogger<PubSoftRateLimitingMiddleware>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<PubSoftRateLimitingMiddleware>>(logger);
        using var provider = services.BuildServiceProvider();

        var middleware = new PubSoftRateLimitingMiddleware(_ => Task.CompletedTask, new RateLimiter());

        var first = CreateContext("172.18.0.1", "203.0.113.7");
        first.RequestServices = provider;
        var second = CreateContext("172.18.0.1", "198.51.100.9");
        second.RequestServices = provider;

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);

        Assert.Single(logger.Entries);
        Assert.Contains("UsePubSoftForwardedHeaders", logger.Entries.Single());
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Enqueue(formatter(state, exception));
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