using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.AspNetCore;
using PubSoft.Core;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task DomainExceptionMapsToBadRequestWithCorrelationId()
    {
        var context = CreateContext();
        context.TraceIdentifier = "trace-domain";
        var middleware = CreateMiddleware(_ => throw new DomainException("Order is closed."), Environments.Production);

        await middleware.InvokeAsync(context);

        using var document = await ReadResponseAsync(context);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("domain_error", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("Order is closed.", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("trace-domain", document.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData(409, StatusCodes.Status409Conflict)]  // 冲突类领域错误可显式声明
    [InlineData(500, StatusCodes.Status400BadRequest)] // 4xx 区间外回退 400——领域错误永不映射 5xx
    public async Task DomainExceptionHonoursDeclared4xxStatusCode(int declared, int expected)
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ => throw new DomainException("Already registered.") { StatusCode = declared },
            Environments.Production);

        await middleware.InvokeAsync(context);

        using var document = await ReadResponseAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal("domain_error", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("Already registered.", document.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(true, StatusCodes.Status400BadRequest, "invalid_argument", "Invalid request.")]
    [InlineData(false, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication required.")]
    public async Task ClientExceptionsMapToSafeResponses(
        bool isArgumentException,
        int statusCode,
        string errorCode,
        string expectedMessage)
    {
        var context = CreateContext();
        Exception exception = isArgumentException
            ? new ArgumentException("Secret input value: private-token")
            : new UnauthorizedAccessException("Private authorization details.");
        var middleware = CreateMiddleware(_ => throw exception, Environments.Production);

        await middleware.InvokeAsync(context);

        using var document = await ReadResponseAsync(context);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(errorCode, document.RootElement.GetProperty("error").GetString());
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("private-token", document.RootElement.GetRawText());
    }

    [Fact]
    public async Task ProductionHidesUnhandledExceptionDetails()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("private database detail"),
            Environments.Production);

        await middleware.InvokeAsync(context);

        using var document = await ReadResponseAsync(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An internal server error occurred.", document.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("stackTrace").ValueKind);
        Assert.DoesNotContain("private database detail", document.RootElement.GetRawText());
    }

    [Fact]
    public async Task DevelopmentIncludesUnhandledExceptionDetails()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(
            _ => throw new InvalidOperationException("development detail"),
            Environments.Development);

        await middleware.InvokeAsync(context);

        using var document = await ReadResponseAsync(context);
        Assert.Equal("development detail", document.RootElement.GetProperty("message").GetString());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("stackTrace").ValueKind);
    }

    [Fact]
    public async Task CancellationAndExceptionsAfterResponseStartPropagate()
    {
        var cancelledContext = CreateContext();
        var cancellationMiddleware = CreateMiddleware(
            _ => throw new OperationCanceledException(),
            Environments.Production);
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancellationMiddleware.InvokeAsync(cancelledContext));

        var responseFeature = new StartedResponseFeature();
        var startedContext = CreateContext();
        startedContext.Features.Set<IHttpResponseFeature>(responseFeature);
        var startedMiddleware = CreateMiddleware(
            _ => throw new InvalidOperationException("response already started"),
            Environments.Production);
        responseFeature.StatusCode = StatusCodes.Status202Accepted;
        responseFeature.HasStarted = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => startedMiddleware.InvokeAsync(startedContext));
        Assert.Equal(StatusCodes.Status202Accepted, startedContext.Response.StatusCode);
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next, string environmentName)
        => new(next, NullLogger<ExceptionHandlingMiddleware>.Instance, new TestHostEnvironment(environmentName));

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "PubSoft.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; set; }
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
