using System.Net;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ResilienceHttpWaveTests
{
    [Fact]
    public void HttpResilienceOptionsDefaultsAndValidation()
    {
        var o = new HttpResilienceOptions();
        Assert.Equal(TimeSpan.FromSeconds(8), o.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), o.PerAttemptTimeout);
        Assert.Null(o.Retry);
        Assert.Null(o.CircuitBreaker);
    }

    [Fact]
    public void HandlerCtorRejectsInvalidOptions()
        => Assert.Throws<ArgumentException>(() =>
            new PubSoftHttpResilienceHandler(new HttpResilienceOptions { Timeout = TimeSpan.Zero }));

    private sealed class StubInner(Func<int, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(factory(Calls));
        }
    }

    [Fact]
    public async Task HandlerRetriesOn5xxThenSucceeds()
    {
        var inner = new StubInner(n => n < 2
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new PubSoftHttpResilienceHandler(new HttpResilienceOptions
        {
            Retry = new RetryPolicy { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) },
        })
        { InnerHandler = inner };
        var res = await new HttpClient(handler).GetAsync("https://x.test/api");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task HandlerGivesUpAfterMaxRetries()
    {
        var inner = new StubInner(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var handler = new PubSoftHttpResilienceHandler(new HttpResilienceOptions
        {
            Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.FromMilliseconds(1) },
        })
        { InnerHandler = inner };
        var res = await new HttpClient(handler).GetAsync("https://x.test/api");
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal(2, inner.Calls); // 1 attempt + 1 retry
    }

    [Fact]
    public void RetryPolicyShouldRetrySemantics()
    {
        var p = new RetryPolicy { MaxRetries = 2 };
        Assert.True(p.ShouldRetry(null, 1, 500));
        Assert.False(p.ShouldRetry(null, 3, 500)); // beyond max
        Assert.False(p.ShouldRetry(null, 1, 403)); // 4xx never retries
        Assert.False(new RetryPolicy { RetryOn5xx = false }.ShouldRetry(null, 1, 500));
        Assert.True(p.ShouldRetry(new HttpRequestException("net"), 1, null)); // connection failure
        Assert.True(p.ShouldRetry(new OperationCanceledException(), 1, null)); // timeout
    }
}
