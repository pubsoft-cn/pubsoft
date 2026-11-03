using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MonitoringTests
{
    [Fact]
    public async Task EmptyTargetListIsHealthyAndDoesNotCreateClient()
    {
        var check = CreateCheck(new MonitoringOptions(), new ThrowingHttpClientFactory());

        Assert.Equal(ProbeHealth.Healthy, await check.CheckAsync());
    }

    [Fact]
    public async Task CheckAsyncAggregatesSuccessfulStatusCodesAgainstConfiguredRatio()
    {
        var handler = new DelegateHandler((request, _) => Task.FromResult(
            new HttpResponseMessage(request.RequestUri!.AbsolutePath == "/ok" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)));
        var options = new MonitoringOptions
        {
            ProbeTargets = ["https://probe.test/ok", "https://probe.test/down"],
            MinProbeSuccessRatio = 0.5
        };

        var check = CreateCheck(options, new TestHttpClientFactory(handler));

        Assert.Equal(ProbeHealth.Healthy, await check.CheckAsync());
    }

    [Fact]
    public async Task CheckAsyncCountsNetworkFailuresAndContinuesToOtherTargets()
    {
        var handler = new DelegateHandler((request, _) => request.RequestUri!.AbsolutePath == "/error"
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("private target detail"))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var options = new MonitoringOptions
        {
            ProbeTargets = ["https://probe.test/error", "https://probe.test/ok"],
            MinProbeSuccessRatio = 0.75
        };

        var check = CreateCheck(options, new TestHttpClientFactory(handler));

        Assert.Equal(ProbeHealth.Unreachable, await check.CheckAsync());
    }

    [Fact]
    public async Task CheckAsyncAppliesTimeoutPerTargetAndContinues()
    {
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/slow")
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var options = new MonitoringOptions
        {
            ProbeTargets = ["https://probe.test/slow", "https://probe.test/ok"],
            MinProbeSuccessRatio = 0.5,
            ProbeTimeout = TimeSpan.FromMilliseconds(30)
        };

        var check = CreateCheck(options, new TestHttpClientFactory(handler));

        Assert.Equal(ProbeHealth.Healthy, await check.CheckAsync());
    }

    [Fact]
    public async Task CheckAsyncPropagatesCallerCancellation()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var check = CreateCheck(new MonitoringOptions
        {
            ProbeTargets = ["https://probe.test/slow"]
        }, new TestHttpClientFactory(handler));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckAsync(cancellation.Token));
    }

    [Fact]
    public void AddPubSoftMonitoringRegistersCheckAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPubSoftMonitoring(options => options.ProbeTargets = ["https://probe.test/health"]);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<HttpProbeHealthCheck>(provider.GetRequiredService<IProbeHealthCheck>());
        Assert.Single(provider.GetRequiredService<IOptions<MonitoringOptions>>().Value.ProbeTargets);
    }

    [Fact]
    public void AddPubSoftMonitoringRejectsInvalidTargetConfiguration()
    {
        var services = new ServiceCollection();
        services.AddPubSoftMonitoring(options => options.ProbeTargets = ["file:///etc/passwd"]);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MonitoringOptions>>().Value);
    }

    private static HttpProbeHealthCheck CreateCheck(MonitoringOptions options, IHttpClientFactory factory)
        => new(factory, Options.Create(options), NullLogger<HttpProbeHealthCheck>.Instance);

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No client expected.");
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }
}