using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData(AppHealthStatus.Healthy, StatusCodes.Status200OK, "Healthy")]
    [InlineData(AppHealthStatus.Degraded, StatusCodes.Status200OK, "Degraded")]
    [InlineData(AppHealthStatus.Unhealthy, StatusCodes.Status503ServiceUnavailable, "Unhealthy")]
    public async Task EndpointReturnsOnlyAggregateState(
        AppHealthStatus status,
        int expectedStatusCode,
        string expectedBodyStatus)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(new HealthCheckAggregator([new FixedHealthCheck(status)]));
        await using var app = builder.Build();
        app.MapPubSoftHealthEndpoint();

        var routeBuilder = (IEndpointRouteBuilder)app;
        var endpoint = Assert.IsType<RouteEndpoint>(Assert.Single(
            routeBuilder.DataSources.SelectMany(dataSource => dataSource.Endpoints)));
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Equal("/health", endpoint.RoutePattern.RawText);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedBodyStatus, document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.DoesNotContain("database", document.RootElement.GetRawText());
        Assert.DoesNotContain("private health details", document.RootElement.GetRawText());
    }

    private sealed class FixedHealthCheck(AppHealthStatus status) : IAppHealthCheck
    {
        public string Name => "database";

        public ValueTask<AppHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AppHealthCheckResult(status, "private health details"));
    }
}