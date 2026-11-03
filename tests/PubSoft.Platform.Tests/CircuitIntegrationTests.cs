using System.Net;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CircuitIntegrationTests
{
    private sealed class FailingInner : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    [Fact]
    public async Task CircuitOpensAfterThresholdAndSkipsCalls()
    {
        var breaker = new CircuitBreaker { Options = new CircuitBreakerOptions { FailureThreshold = 2 } };
        var inner = new FailingInner();
        var handler = new PubSoftHttpResilienceHandler(
            new HttpResilienceOptions { CircuitBreaker = new CircuitBreakerOptions() }, breaker)
        { InnerHandler = inner };
        var http = new HttpClient(handler);

        // circuit-closed: inner gets hit, RecordFailure path
        var r1 = await http.GetAsync("https://x.test/api");
        Assert.Equal(HttpStatusCode.InternalServerError, r1.StatusCode);
        var callsBefore = inner.Calls;
        Assert.True(callsBefore > 0);

        breaker.RecordFailure("x.test");
        breaker.RecordFailure("x.test"); // hits threshold → open

        // circuit-open: HttpRequestException before inner call
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://x.test/api"));
        Assert.Equal(callsBefore, inner.Calls); // inner NOT invoked
    }

    [Fact]
    public void TryAcquireOpenRejectsRequests()
    {
        var breaker = new CircuitBreaker { Options = new CircuitBreakerOptions { FailureThreshold = 1 } };
        breaker.RecordFailure("h1");
        Assert.False(breaker.TryAcquire("h1")); // open
        Assert.True(breaker.TryAcquire("h2")); // different host still closed
    }

    [Fact]
    public void RecordSuccessResetsCircuit()
    {
        var breaker = new CircuitBreaker { Options = new CircuitBreakerOptions { FailureThreshold = 2 } };
        breaker.RecordFailure("h1");
        breaker.RecordSuccess("h1");
        breaker.RecordFailure("h1");
        Assert.True(breaker.TryAcquire("h1")); // still closed — counter reset
    }
}
