using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PubSoft.AspNetCore;
using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AspNetCoreSecurityMiddlewareTests
{
    [Fact]
    public async Task HmacMiddleware_RejectsMissingHeadersByDefault()
    {
        var context = CreateContext();
        var nextCalled = false;
        var middleware = CreateHmacMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task HmacMiddleware_ValidatesSignatureAndPreservesBodyForNextMiddleware()
    {
        const string secret = "app secret";
        const string body = "request body";
        var context = CreateContext(body);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = HmacSignatureValidator.ComputeBodyHash(Encoding.UTF8.GetBytes(body));
        SetSignatureHeaders(context, timestamp, "nonce-1", HmacSignatureValidator.BuildSignature(
            secret, "GET", "/resource", timestamp, "nonce-1", bodyHash));
        var nextCalled = false;
        var middleware = CreateHmacMiddleware(_ =>
        {
            nextCalled = true;
            Assert.Equal(body, new StreamReader(context.Request.Body, leaveOpen: true).ReadToEnd());
            return Task.CompletedTask;
        }, requireBodyHash: true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal("app-1", context.Items["PubSoft.AppId"]);
    }

    [Fact]
    public async Task HmacMiddleware_RejectsOversizedBody()
    {
        var context = CreateContext("12345");
        context.Request.ContentLength = 5;
        SetSignatureHeaders(context, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "n", new string('0', 64));
        var middleware = CreateHmacMiddleware(_ => Task.CompletedTask, requireBodyHash: true, maximumBodyBytes: 4);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task HmacMiddleware_RejectsReplaysBeforeCallingNext()
    {
        var context = CreateContext();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacSignatureValidator.BuildSignature("app secret", "GET", "/resource", timestamp, "same-nonce");
        SetSignatureHeaders(context, timestamp, "same-nonce", signature);
        var validator = new HmacSignatureValidator(new MemoryNonceStore());
        var nextCount = 0;
        var middleware = new HmacValidationMiddleware(
            _ =>
            {
                nextCount++;
                return Task.CompletedTask;
            },
            new TestSecretResolver(),
            validator,
            Options.Create(new HmacValidationOptions()));

        await middleware.InvokeAsync(context);
        var replayContext = CreateContext();
        SetSignatureHeaders(replayContext, timestamp, "same-nonce", signature);
        await middleware.InvokeAsync(replayContext);

        Assert.Equal(1, nextCount);
        Assert.Equal(StatusCodes.Status401Unauthorized, replayContext.Response.StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_AddDefaultsAndOnlySendHstsForHttps()
    {
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        var httpContext = CreateContext();
        await middleware.InvokeAsync(httpContext);
        var httpsContext = CreateContext();
        httpsContext.Request.IsHttps = true;
        await middleware.InvokeAsync(httpsContext);

        Assert.Equal("nosniff", httpContext.Response.Headers["X-Content-Type-Options"]);
        Assert.False(httpContext.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.Contains("Strict-Transport-Security", httpsContext.Response.Headers.Keys);
    }

    [Fact]
    public void SecurityHeaders_RejectCrLfInConfiguredValues()
    {
        Assert.Throws<ArgumentException>(() => new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            new SecurityHeadersOptions { ContentSecurityPolicy = "default-src 'self'\r\nX-Evil: yes" }));
    }

    [Fact]
    public void HmacMiddleware_RejectsUnboundedBodyLimitConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateHmacMiddleware(
            _ => Task.CompletedTask,
            requireBodyHash: true,
            maximumBodyBytes: 16 * 1024 * 1024 + 1));
    }

    private static HmacValidationMiddleware CreateHmacMiddleware(
        RequestDelegate next,
        bool requireBodyHash = false,
        int maximumBodyBytes = 1_048_576)
        => new(
            next,
            new TestSecretResolver(),
            new HmacSignatureValidator(new MemoryNonceStore()),
            Options.Create(new HmacValidationOptions
            {
                RequireBodyHash = requireBodyHash,
                MaximumBodyBytes = maximumBodyBytes
            }));

    private static DefaultHttpContext CreateContext(string? body = null)
    {
        var context = new DefaultHttpContext();
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }
        context.Request.Method = "GET";
        context.Request.Path = "/resource";
        return context;
    }

    private static void SetSignatureHeaders(HttpContext context, string timestamp, string nonce, string signature)
    {
        context.Request.Method = "GET";
        context.Request.Headers[HmacSignatureHeaders.AppId] = "app-1";
        context.Request.Headers[HmacSignatureHeaders.Signature] = signature;
        context.Request.Headers[HmacSignatureHeaders.Timestamp] = timestamp;
        context.Request.Headers[HmacSignatureHeaders.Nonce] = nonce;
    }

    private sealed class TestSecretResolver : IAppSecretResolver
    {
        public ValueTask<string?> ResolveSecretAsync(string appId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(appId == "app-1" ? "app secret" : null);
    }
}