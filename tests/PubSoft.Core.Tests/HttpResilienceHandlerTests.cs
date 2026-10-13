using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class HttpResilienceHandlerTests
{
    [Fact]
    public async Task RetriesRetryableStatusForSafeRequestAndDisposesIntermediateResponse()
    {
        var firstContent = new TrackingContent();
        var firstResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = firstContent };
        var responseIndex = 0;
        var inner = new RecordingHandler(_ => Task.FromResult(
            Interlocked.Increment(ref responseIndex) == 1 ? firstResponse : new HttpResponseMessage(HttpStatusCode.OK)));
        var handler = CreateHandler(inner, new HttpResilienceOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            PerAttemptTimeout = TimeSpan.FromSeconds(1),
            Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.Zero }
        });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://service.test/status?secret=value");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.RequestCount);
        Assert.True(firstContent.WasDisposed);
        Assert.NotSame(inner.Requests.ElementAt(0), inner.Requests.ElementAt(1));
    }

    [Fact]
    public async Task DoesNotRetryUnsafeOrBodyBearingRequests()
    {
        var unsafeInner = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var unsafeHandler = CreateHandler(unsafeInner, new HttpResilienceOptions
        {
            Retry = new RetryPolicy { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
        });
        using var unsafeClient = new HttpClient(unsafeHandler);
        using var unsafeResponse = await unsafeClient.PostAsync("https://service.test/write", new StringContent("payload"));

        var bodyInner = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var bodyHandler = CreateHandler(bodyInner, new HttpResilienceOptions
        {
            Retry = new RetryPolicy { MaxRetries = 3, InitialDelay = TimeSpan.Zero }
        });
        using var bodyClient = new HttpClient(bodyHandler);
        using var bodyResponse = await bodyClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://service.test/read")
        {
            Content = new StringContent("payload")
        });

        Assert.Equal(1, unsafeInner.RequestCount);
        Assert.Equal(1, bodyInner.RequestCount);
    }

    [Fact]
    public async Task PerAttemptTimeoutRetriesAndCallerCancellationReleasesHalfOpenProbe()
    {
        var timeoutRequestCount = 0;
        var timeoutInner = new RecordingHandler(async cancellationToken =>
        {
            if (Interlocked.Increment(ref timeoutRequestCount) == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var timeoutHandler = CreateHandler(timeoutInner, new HttpResilienceOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            PerAttemptTimeout = TimeSpan.FromMilliseconds(20),
            Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.Zero }
        });
        using var timeoutClient = new HttpClient(timeoutHandler);

        using var timeoutResponse = await timeoutClient.GetAsync("https://service.test/timeout");
        Assert.Equal(HttpStatusCode.OK, timeoutResponse.StatusCode);
        Assert.Equal(2, timeoutInner.RequestCount);

        var breaker = new CircuitBreaker
        {
            Options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                OpenTimeout = TimeSpan.FromMilliseconds(20)
            }
        };
        var calls = 0;
        var circuitInner = new RecordingHandler(async cancellationToken =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (call == 2)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var circuitClient = new HttpClient(CreateHandler(
            circuitInner,
            new HttpResilienceOptions
            {
                Timeout = TimeSpan.FromSeconds(2),
                PerAttemptTimeout = TimeSpan.FromSeconds(1),
                CircuitBreaker = breaker.Options
            },
            breaker));

        using (var failed = await circuitClient.GetAsync("https://service.test/circuit"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        await Task.Delay(30);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => circuitClient.GetAsync(
            "https://service.test/circuit", cancellation.Token));

        using var recovered = await circuitClient.GetAsync("https://service.test/circuit");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(3, circuitInner.RequestCount);
    }

    [Fact]
    public async Task TotalTimeoutCancelsRequestAndRecordsCircuitFailure()
    {
        var breaker = new CircuitBreaker
        {
            Options = new CircuitBreakerOptions { FailureThreshold = 1 }
        };
        var calls = 0;
        var inner = new RecordingHandler(async cancellationToken =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(CreateHandler(
            inner,
            new HttpResilienceOptions
            {
                Timeout = TimeSpan.FromMilliseconds(120),
                PerAttemptTimeout = TimeSpan.FromMilliseconds(100),
                Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.FromMilliseconds(40) },
                CircuitBreaker = breaker.Options
            },
            breaker));

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync("https://service.test/overall-timeout"));

        Assert.Equal(2, inner.RequestCount);
        Assert.Equal(CircuitBreaker.State.Open, breaker.GetState("service.test"));
    }

    [Fact]
    public void HttpClientRegistrationDisablesBuiltInTimeoutAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("resilient")
            .AddPubSoftHttpResilience(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(4);
                options.PerAttemptTimeout = TimeSpan.FromSeconds(3);
            });
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("resilient");
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);

        Assert.Throws<ArgumentException>(() => services.AddHttpClient("invalid")
            .AddPubSoftHttpResilience(options => options.PerAttemptTimeout = TimeSpan.FromSeconds(9)));
    }

    private static PubSoftHttpResilienceHandler CreateHandler(
        HttpMessageHandler inner,
        HttpResilienceOptions? options = null,
        CircuitBreaker? circuitBreaker = null)
        => new(options ?? new HttpResilienceOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            PerAttemptTimeout = TimeSpan.FromSeconds(1)
        }, circuitBreaker)
        {
            InnerHandler = inner
        };

    private sealed class RecordingHandler(
        Func<CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);
        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            Interlocked.Increment(ref _requestCount);
            return responseFactory(cancellationToken);
        }
    }

    private sealed class TrackingContent : ByteArrayContent
    {
        public TrackingContent() : base([])
        {
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed |= disposing;
            base.Dispose(disposing);
        }
    }
}