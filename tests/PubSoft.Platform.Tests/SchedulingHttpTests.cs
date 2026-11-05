using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PubSoft.Resilience;
using PubSoft.Scheduling;
using PubSoft.Scheduling.Http;
using PubSoft.Tokens;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingHttpTests
{
    [Fact]
    public async Task ProbeSendsConfiguredRequestAndUsesTokenAsynchronously()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var tokens = new RecordingTokenProvider("token-value");
        var executor = CreateProbe(handler, tokens);
        var job = CreateJob(new HttpExecutorConfig
        {
            Method = "POST",
            Url = "https://service.test/probe",
            Body = "{\"check\":true}",
            Token = new TokenRef
            {
                CredentialKey = "service",
                HeaderName = "X-Service-Token",
                HeaderPrefix = "Token "
            }
        });

        var result = await executor.ExecuteAsync(job, default);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("POST", handler.Methods.Single());
        Assert.Equal("{\"check\":true}", handler.Bodies.Single());
        Assert.Equal("Token token-value", handler.TokenHeaders.Single());
        Assert.Equal("service", tokens.RequestedKeys.Single());
    }

    [Fact]
    public async Task ProbeNeverRetriesEvenWhenRetryPolicyIsConfigured()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var executor = CreateProbe(handler);
        var job = CreateJob(new HttpExecutorConfig
        {
            Url = "https://service.test/probe",
            Retry = new RetryPolicy { MaxRetries = 2, InitialDelay = TimeSpan.Zero }
        });

        var result = await executor.ExecuteAsync(job, default);

        Assert.False(result.Success);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ProbeDistinguishesEndpointTimeoutFromCallerCancellation()
    {
        var timeoutHandler = new RecordingHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var timeoutExecutor = CreateProbe(timeoutHandler);
        var timeoutJob = CreateJob(new HttpExecutorConfig
        {
            Url = "https://service.test/timeout",
            Timeout = TimeSpan.FromMilliseconds(20)
        });

        var timeoutResult = await timeoutExecutor.ExecuteAsync(timeoutJob, default);

        Assert.False(timeoutResult.Success);
        Assert.Equal("timeout", timeoutResult.ErrorMessage);

        var cancelHandler = new RecordingHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var cancelExecutor = CreateProbe(cancelHandler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelExecutor.ExecuteAsync(
            CreateJob(new HttpExecutorConfig { Url = "https://service.test/cancel" }), cancellation.Token));
    }

    [Fact]
    public async Task ResilientExecutorRetriesRetryableStatusAndReturnsSuccess()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var executor = CreateResilient(handler);
        var job = CreateJob(new HttpExecutorConfig
        {
            Url = "https://service.test/action",
            Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.FromMilliseconds(30) }
        });

        var result = await executor.ExecuteAsync(job, default);

        Assert.True(result.Success);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, handler.RequestCount);
        Assert.True(result.DelayMs >= 20);
    }

    [Fact]
    public async Task ResilientExecutorDoesNotRetryPostUnlessUnsafeRetriesAreAllowed()
    {
        var defaultHandler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var defaultExecutor = CreateResilient(defaultHandler);
        var retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.Zero };
        var defaultResult = await defaultExecutor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Method = "POST",
            Url = "https://service.test/action",
            Body = "{\"action\":true}",
            Retry = retry
        }), default);

        Assert.False(defaultResult.Success);
        Assert.Equal(1, defaultHandler.RequestCount);
        Assert.Equal("{\"action\":true}", defaultHandler.Bodies.Single());

        var optInHandler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var optInExecutor = CreateResilient(optInHandler);
        var optInResult = await optInExecutor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Method = "POST",
            Url = "https://service.test/action",
            Body = "{\"action\":true}",
            Retry = retry,
            AllowUnsafeRetries = true
        }), default);

        Assert.True(optInResult.Success);
        Assert.Equal(2, optInHandler.RequestCount);
        Assert.All(optInHandler.Bodies, body => Assert.Equal("{\"action\":true}", body));
    }

    [Fact]
    public async Task ResilientExecutorOpensCircuitAfterFailedExecution()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var executor = CreateResilient(handler, new HttpExecutorOptions { CircuitFailureThreshold = 1 });
        var job = CreateJob(new HttpExecutorConfig { Url = "https://service.test/action" });

        var first = await executor.ExecuteAsync(job, default);
        var second = await executor.ExecuteAsync(job, default);

        Assert.False(first.Success);
        Assert.Equal("circuit open", second.ErrorMessage);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CallerCancellationReleasesHalfOpenCircuitProbe()
    {
        var breaker = new CircuitBreaker
        {
            Options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                OpenTimeout = TimeSpan.FromMilliseconds(20)
            }
        };
        var handler = new RecordingHandler(
            HttpStatusCode.ServiceUnavailable,
            (Func<CancellationToken, Task<HttpResponseMessage>>)(async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            HttpStatusCode.OK);
        var executor = new ResilientExecutor(
            new TestHttpClientFactory(handler),
            Options.Create(new HttpExecutorOptions()),
            breaker);
        var job = CreateJob(new HttpExecutorConfig { Url = "https://service.test/circuit" });
        await executor.ExecuteAsync(job, default);
        await Task.Delay(30);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(job, cancellation.Token));

        var recovered = await executor.ExecuteAsync(job, default);
        Assert.True(recovered.Success);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task InvalidOrCredentialBearingUrlsAreRejectedWithoutSending()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var executor = CreateProbe(handler);
        var job = CreateJob(new HttpExecutorConfig { Url = "https://user:password@service.test/action" });

        var result = await executor.ExecuteAsync(job, default);

        Assert.False(result.Success);
        Assert.Equal("Invalid HttpExecutorConfig", result.ErrorMessage);
        Assert.Equal(0, handler.RequestCount);
    }

    // ── SSRF egress guard ──

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]   // cloud metadata
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://10.1.2.3/internal")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    public async Task PrivateLiteralTargetsAreRejectedWithoutSending(string url)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var executor = CreateProbe(handler);
        var job = CreateJob(new HttpExecutorConfig { Url = url });

        var result = await executor.ExecuteAsync(job, default);

        Assert.False(result.Success);
        Assert.Equal("Invalid HttpExecutorConfig", result.ErrorMessage);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NonAllowlistedHostIsRejectedWithoutSending()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var executor = CreateProbe(handler, options: new HttpExecutorOptions
        {
            AllowedHosts = { "api.example.com", ".corp.internal" }
        });
        var job = CreateJob(new HttpExecutorConfig { Url = "https://evil.test/x" });

        var result = await executor.ExecuteAsync(job, default);

        Assert.False(result.Success);
        Assert.Equal("Invalid HttpExecutorConfig", result.ErrorMessage);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task AllowlistedExactAndSuffixHostsAreAccepted()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var executor = CreateProbe(handler, options: new HttpExecutorOptions
        {
            AllowedHosts = { "api.example.com", ".corp.internal" }
        });

        foreach (var url in new[]
        {
            "https://api.example.com/a",
            "https://node1.corp.internal/b",
            "https://corp.internal/c", // suffix wildcard covers the bare domain
        })
        {
            var result = await executor.ExecuteAsync(
                CreateJob(new HttpExecutorConfig { Url = url }), default);
            Assert.True(result.Success, url);
        }
        Assert.Equal(3, handler.RequestCount);
    }

    [Theory]
    [InlineData("0.0.0.0", true)]
    [InlineData("10.9.9.9", true)]
    [InlineData("100.64.0.1", true)]   // CGNAT
    [InlineData("100.63.255.255", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("192.0.2.1", true)]    // TEST-NET-1
    [InlineData("198.51.100.1", true)] // TEST-NET-2
    [InlineData("198.51.99.1", false)]
    [InlineData("203.0.113.1", true)]  // TEST-NET-3
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("ff02::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("100::1", true)]           // discard-only
    [InlineData("64:ff9b:1::1", true)]     // v4v6 doc range
    [InlineData("2001:2::1", true)]        // benchmarking
    [InlineData("2001:4:112::1", true)]    // AS112
    [InlineData("2001:10::1", true)]       // ORCHID
    [InlineData("2001:20::1", true)]       // ORCHIDv2
    [InlineData("2001:30::1", true)]       // DRIP
    [InlineData("2001:db8::1", true)]      // documentation
    [InlineData("3fff::1", true)]          // documentation
    [InlineData("5f00::1", true)]          // SRv6 SIDs
    [InlineData("2001:4860:4860::8888", false)] // Google public DNS
    [InlineData("2606:4700:4700::1111", false)]
    public void BlockedAddressClassification(string address, bool blocked)
        => Assert.Equal(blocked, HttpEgressGuard.IsBlockedAddress(IPAddress.Parse(address)));

    // ── manual redirect following ──

    private static HttpResponseMessage RedirectTo(string location)
        => new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    [Fact]
    public async Task RedirectToNonAllowlistedHostIsBlocked()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "api.example.com"
            ? RedirectTo("https://evil.test/cb")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions
        {
            AllowedHosts = { "api.example.com" },
            FollowRedirects = true,
        });

        var result = await executor.ExecuteAsync(
            CreateJob(new HttpExecutorConfig { Url = "https://api.example.com/start" }), default);

        Assert.False(result.Success);
        Assert.Equal("redirect target blocked", result.ErrorMessage);
        Assert.Single(handler.RequestedHosts); // evil.test never contacted
    }

    [Fact]
    public async Task RedirectToLiteralPrivateAddressIsBlocked()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery == "/start"
            ? RedirectTo("http://169.254.169.254/latest")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions { FollowRedirects = true });

        var result = await executor.ExecuteAsync(
            CreateJob(new HttpExecutorConfig { Url = "https://public.test/start" }), default);

        Assert.False(result.Success);
        Assert.Equal("redirect target blocked", result.ErrorMessage);
        Assert.Single(handler.RequestedHosts);
    }

    [Fact]
    public async Task CrossHostRedirectStripsTokenHeader()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "a.example.com"
            ? RedirectTo("https://b.example.com/land")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var tokens = new RecordingTokenProvider("secret-token");
        var executor = CreateProbe(handler, tokens, new HttpExecutorOptions
        {
            AllowedHosts = { ".example.com" },
            FollowRedirects = true,
        });

        var result = await executor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Url = "https://a.example.com/start",
            Token = new TokenRef { CredentialKey = "svc", HeaderName = "X-Service-Token" },
        }), default);

        Assert.True(result.Success);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("Bearer secret-token", handler.TokenHeaders.ElementAt(0));
        Assert.Null(handler.TokenHeaders.ElementAt(1)); // credential not leaked cross-host
    }

    [Fact]
    public async Task SameHostRedirectKeepsTokenAndDowngradesPost()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery == "/start"
            ? RedirectTo("https://a.example.com/land")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var tokens = new RecordingTokenProvider("secret-token");
        var executor = CreateProbe(handler, tokens, new HttpExecutorOptions { FollowRedirects = true });

        var result = await executor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Method = "POST",
            Url = "https://a.example.com/start",
            Body = "{}",
            Token = new TokenRef { CredentialKey = "svc", HeaderName = "X-Service-Token" },
        }), default);

        Assert.True(result.Success);
        Assert.Equal("Bearer secret-token", handler.TokenHeaders.ElementAt(1)); // same host keeps credential
        Assert.Equal("GET", handler.Methods.ElementAt(1)); // 302 downgrades POST to GET
    }

    [Fact]
    public async Task RedirectLoopFailsAfterBudget()
    {
        var handler = new RecordingHandler(_ => RedirectTo("https://a.example.com/loop"));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions { FollowRedirects = true });

        var result = await executor.ExecuteAsync(
            CreateJob(new HttpExecutorConfig { Url = "https://a.example.com/loop" }), default);

        Assert.False(result.Success);
        Assert.Equal("redirect limit exceeded", result.ErrorMessage);
        Assert.True(handler.RequestCount <= 10);
    }

    [Fact]
    public void ProxyPlusPrivateBlockingIsRejectedAtValidation()
        => Assert.False(new HttpExecutorOptions
        {
            UseSystemProxy = true,
            BlockPrivateNetworks = true,
        }.IsValid());

    [Fact]
    public async Task ConnectBlockingRefusesPrivateLiteralAtSocketLevel()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => HttpEgressGuard.ConnectBlockingAsync("169.254.169.254", 80, CancellationToken.None).AsTask());
        Assert.Contains("non-public", exception.Message);
    }

    [Fact]
    public void RegistrationAddsBothExecutorsAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddPubSoftHttpExecutors(options => options.DefaultTimeout = TimeSpan.FromSeconds(3));
        using var provider = services.BuildServiceProvider();

        var executors = provider.GetServices<IJobExecutor>().ToArray();
        Assert.Contains(executors, executor => executor.Type == "Http.Probe");
        Assert.Contains(executors, executor => executor.Type == "Http.Resilient");
        Assert.Equal(TimeSpan.FromSeconds(3), provider.GetRequiredService<IOptions<HttpExecutorOptions>>().Value.DefaultTimeout);
    }

    private static ProbeExecutor CreateProbe(
        HttpMessageHandler handler,
        ITokenProvider? tokenProvider = null,
        HttpExecutorOptions? options = null)
        => new(new TestHttpClientFactory(handler), Options.Create(options ?? new HttpExecutorOptions()), tokenProvider);

    private static ResilientExecutor CreateResilient(
        HttpMessageHandler handler,
        HttpExecutorOptions? options = null)
    {
        options ??= new HttpExecutorOptions();
        return new ResilientExecutor(
            new TestHttpClientFactory(handler),
            Options.Create(options),
            new CircuitBreaker
            {
                Options = new CircuitBreakerOptions
                {
                    FailureThreshold = options.CircuitFailureThreshold,
                    OpenTimeout = options.CircuitOpenTimeout
                }
            });
    }

    private static ScheduledJob CreateJob(HttpExecutorConfig config)
        => new() { ExecutorConfig = JsonSerializer.SerializeToElement(config) };

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingTokenProvider(string? token) : ITokenProvider
    {
        public ConcurrentQueue<string> RequestedKeys { get; } = new();

        public Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedKeys.Enqueue(credentialKey);
            return Task.FromResult(token);
        }

        public Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task CrossHostRedirectDropsConfiguredHeaders()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "a.example.com"
            ? RedirectTo("https://b.example.com/land")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions
        {
            AllowedHosts = { ".example.com" },
            FollowRedirects = true,
        });

        var result = await executor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Url = "https://a.example.com/start",
            Headers = { ["X-Custom-Key"] = "secret-value" },
        }), default);

        Assert.True(result.Success);
        Assert.Equal("secret-value", handler.CustomHeaders.ElementAt(0));
        Assert.Null(handler.CustomHeaders.ElementAt(1));
    }

    [Fact]
    public async Task SchemeDowngradeRedirectDropsConfiguredHeaders()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Scheme == "https"
            ? RedirectTo("http://a.example.com/land")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions { FollowRedirects = true });

        var result = await executor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Url = "https://a.example.com/start",
            Headers = { ["X-Custom-Key"] = "secret-value" },
        }), default);

        Assert.True(result.Success);
        Assert.Equal("secret-value", handler.CustomHeaders.ElementAt(0));
        Assert.Null(handler.CustomHeaders.ElementAt(1));
    }

    [Fact]
    public async Task SameHostDifferentPortRedirectDropsConfiguredHeaders()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Port == 443
            ? RedirectTo("https://a.example.com:8443/land")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions { FollowRedirects = true });

        var result = await executor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Url = "https://a.example.com/start",
            Headers = { ["X-Custom-Key"] = "secret-value" },
        }), default);

        Assert.True(result.Success);
        Assert.Equal("secret-value", handler.CustomHeaders.ElementAt(0));
        Assert.Null(handler.CustomHeaders.ElementAt(1));
    }

    [Fact]
    public async Task SameOriginRedirectKeepsConfiguredHeaders()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery == "/start"
            ? RedirectTo("https://a.example.com/land")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateProbe(handler, options: new HttpExecutorOptions { FollowRedirects = true });

        var result = await executor.ExecuteAsync(CreateJob(new HttpExecutorConfig
        {
            Url = "https://a.example.com/start",
            Headers = { ["X-Custom-Key"] = "secret-value" },
        }), default);

        Assert.True(result.Success);
        Assert.Equal("secret-value", handler.CustomHeaders.ElementAt(0));
        Assert.Equal("secret-value", handler.CustomHeaders.ElementAt(1));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;
        private int _requestCount;

        public RecordingHandler(params object[] responses)
        {
            _responseFactory = (_, cancellationToken) =>
            {
                var response = responses[Math.Min(RequestCount - 1, responses.Length - 1)];
                return response switch
                {
                    HttpStatusCode status => Task.FromResult(new HttpResponseMessage(status)),
                    Func<CancellationToken, Task<HttpResponseMessage>> factory => factory(cancellationToken),
                    _ => throw new InvalidOperationException("Unexpected HTTP test response.")
                };
            };
        }

        public RecordingHandler(Func<CancellationToken, Task<HttpResponseMessage>> responseFactory)
            => _responseFactory = (_, ct) => responseFactory(ct);

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            => _responseFactory = (request, _) => Task.FromResult(responseFactory(request));

        public int RequestCount => Volatile.Read(ref _requestCount);
        public ConcurrentQueue<string> Methods { get; } = new();
        public ConcurrentQueue<string?> Bodies { get; } = new();
        public ConcurrentQueue<string?> TokenHeaders { get; } = new();
        public ConcurrentQueue<string?> CustomHeaders { get; } = new();
        public ConcurrentQueue<string> RequestedHosts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            Methods.Enqueue(request.Method.Method);
            Bodies.Enqueue(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            TokenHeaders.Enqueue(request.Headers.TryGetValues("X-Service-Token", out var values) ? values.Single() : null);
            CustomHeaders.Enqueue(request.Headers.TryGetValues("X-Custom-Key", out var cvals) ? cvals.Single() : null);
            RequestedHosts.Enqueue(request.RequestUri!.Host);
            return await _responseFactory(request, cancellationToken);
        }
    }

    [Fact]
    public async Task OversizedResponseIsRejectedByDeclaredLengthCap()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 128)),
        });
        var executor = CreateResilient(handler, new HttpExecutorOptions { MaxResponseBytes = 64 });

        var result = await executor.ExecuteAsync(
            CreateJob(new HttpExecutorConfig { Url = "https://service.test/big" }), default);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ResponseWithinDeclaredLengthCapPasses()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok"),
        });
        var executor = CreateResilient(handler, new HttpExecutorOptions { MaxResponseBytes = 64 });

        var result = await executor.ExecuteAsync(
            CreateJob(new HttpExecutorConfig { Url = "https://service.test/ok" }), default);

        Assert.True(result.Success);
    }
}