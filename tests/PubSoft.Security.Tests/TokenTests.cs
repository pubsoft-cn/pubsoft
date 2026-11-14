using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Caching;
using PubSoft.Tokens;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class TokenTests
{
    [Fact]
    public async Task ProviderCachesTokenAndUsesConfiguredDynamicExpiry()
    {
        var handler = new QueueHandler(_ => Task.FromResult(JsonResponse("{\"data\":{\"access_token\":\"token-a\",\"expires_in\":120}}")));
        var cache = new RecordingCache();
        var provider = CreateProvider(handler, cache, new TokenCredential
        {
            Url = "https://auth.test/token",
            TokenPath = "data.access_token",
            ExpiryPath = "data.expires_in",
            Expiry = TimeSpan.FromMinutes(10),
            RefreshBefore = TimeSpan.FromSeconds(10)
        });

        Assert.Equal("token-a", await provider.GetTokenAsync("primary"));
        Assert.Equal("token-a", await provider.GetTokenAsync("primary"));
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(TimeSpan.FromSeconds(120), cache.LastExpiry);
    }

    [Fact]
    public async Task ProviderSingleFlightsConcurrentCacheMisses()
    {
        var handler = new QueueHandler(async _ =>
        {
            await Task.Delay(30);
            return JsonResponse("{\"token\":\"shared\"}");
        });
        var provider = CreateProvider(handler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token"
        });

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.GetTokenAsync("primary")));

        Assert.All(tokens, token => Assert.Equal("shared", token));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ProviderRefreshesAtConfiguredRefreshBoundary()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var handler = new QueueHandler(_ => Task.FromResult(JsonResponse("{\"token\":\"rotated\"}")));
        var provider = CreateProvider(handler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token",
            Expiry = TimeSpan.FromMinutes(10),
            RefreshBefore = TimeSpan.FromMinutes(1)
        }, clock);

        Assert.Equal("rotated", await provider.GetTokenAsync("primary"));
        clock.Advance(TimeSpan.FromMinutes(8));
        Assert.Equal("rotated", await provider.GetTokenAsync("primary"));
        Assert.Equal(1, handler.RequestCount);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("rotated", await provider.GetTokenAsync("primary"));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ProviderReturnsNullForMissingCredentialOrMalformedResponse()
    {
        var handler = new QueueHandler(_ => Task.FromResult(JsonResponse("{\"access_token\":\"x\"}")));
        var provider = CreateProvider(handler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token",
            TokenPath = "data.token"
        });

        Assert.Null(await provider.GetTokenAsync("missing"));
        Assert.Null(await provider.GetTokenAsync("primary"));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ProviderDoesNotLogEndpointUriFromTransportException()
    {
        const string endpoint = "https://auth.test/token?client_secret=do-not-log";
        var handler = new QueueHandler(_ => throw new HttpRequestException($"Request failed for {endpoint}"));
        var logger = new CapturingLogger<HttpTokenProvider>();
        var provider = CreateProvider(handler, new RecordingCache(), new TokenCredential
        {
            Url = endpoint
        }, logger: logger);

        Assert.Null(await provider.GetTokenAsync("primary"));

        var log = string.Join(Environment.NewLine, logger.Entries);
        Assert.Contains("Token endpoint request failed.", log);
        Assert.DoesNotContain(endpoint, log);
        Assert.DoesNotContain("do-not-log", log);
    }

    [Fact]
    public async Task ProviderPropagatesCallerCancellationAndTimesOutEndpoint()
    {
        var callerCancellationHandler = new QueueHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{\"token\":\"never\"}");
        });
        var callerCancellationProvider = CreateProvider(callerCancellationHandler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token",
            Timeout = TimeSpan.FromSeconds(2)
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerCancellationProvider.GetTokenAsync("primary", cancellation.Token));

        var timeoutHandler = new QueueHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{\"token\":\"never\"}");
        });
        var timeoutProvider = CreateProvider(timeoutHandler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token",
            Timeout = TimeSpan.FromMilliseconds(20)
        });
        Assert.Null(await timeoutProvider.GetTokenAsync("primary"));
    }

    [Fact]
    public async Task DelegatingHandlerRetries401OnceWithReplayableBodyAndRefreshedToken()
    {
        var provider = new FakeTokenProvider("old", "new");
        var inner = new RecordingHandler(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.OK);
        using var handler = new TokenDelegatingHandler(provider, Options.Create(new TokenHandlerOptions
        {
            DefaultCredentialKey = "primary"
        }))
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://service.test/action")
        {
            Content = new StringContent("payload")
        };
        request.EnableTokenUnauthorizedRetry();

        using var response = await client.SendAsync(request, default);

        Assert.Equal(2, inner.RequestCount);
        Assert.Equal(["Bearer old", "Bearer new"], inner.AuthorizationValues);
        Assert.Equal(["payload", "payload"], inner.Bodies);
        Assert.Equal(1, provider.InvalidationCount);
        Assert.Equal(2, provider.GetCount);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DelegatingHandlerDoesNotRetry401UnlessRequestOptsIn()
    {
        var provider = new FakeTokenProvider("old", "new");
        var inner = new RecordingHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = new TokenDelegatingHandler(provider, Options.Create(new TokenHandlerOptions
        {
            DefaultCredentialKey = "primary"
        }))
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://service.test/action")
        {
            Content = new StringContent("payload")
        };

        using var response = await client.SendAsync(request, default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.RequestCount);
        Assert.Equal(["payload"], inner.Bodies);
        Assert.Equal(0, provider.InvalidationCount);
        Assert.Equal(1, provider.GetCount);
    }

    [Fact]
    public async Task DelegatingHandlerUsesRequestOptionsOverridesAndDoesNotRetryTwice()
    {
        var provider = new FakeTokenProvider("token");
        var inner = new RecordingHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = new TokenDelegatingHandler(provider, Options.Create(new TokenHandlerOptions
        {
            DefaultCredentialKey = "default"
        }), "client", "Authorization", "Bearer ")
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://service.test/")
            .SetTokenCredentialKey("request")
            .SetTokenHeaderName("X-Access")
            .SetTokenHeaderPrefix("Token ")
            .EnableTokenUnauthorizedRetry();

        using var response = await client.SendAsync(request, default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["request", "request"], provider.RequestedKeys.Order().ToArray());
        Assert.Equal(2, inner.RequestCount);
        Assert.Equal(["Token token", "Token token"], inner.AccessValues);
    }

    [Fact]
    public async Task DelegatingHandlerSkipsAuthenticationWhenNoCredentialIsConfigured()
    {
        var provider = new FakeTokenProvider("unused");
        var inner = new RecordingHandler(HttpStatusCode.OK);
        using var handler = new TokenDelegatingHandler(provider, Options.Create(new TokenHandlerOptions()))
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://service.test/");

        using var response = await client.SendAsync(request, default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, provider.GetCount);
        Assert.Equal(1, inner.RequestCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DelegatingHandlerFailsClosedWhenConfiguredCredentialHasNoToken(string? token)
    {
        var provider = new FakeTokenProvider([token]);
        var inner = new RecordingHandler(HttpStatusCode.OK);
        using var handler = new TokenDelegatingHandler(provider, Options.Create(new TokenHandlerOptions
        {
            DefaultCredentialKey = "primary"
        }))
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://service.test/");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request, default));

        Assert.Equal(1, provider.GetCount);
        Assert.Equal(0, inner.RequestCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DelegatingHandlerDoesNotRetryWithAnEmptyRefreshedToken(string? refreshedToken)
    {
        var provider = new FakeTokenProvider("old", refreshedToken);
        var inner = new RecordingHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = new TokenDelegatingHandler(provider, Options.Create(new TokenHandlerOptions
        {
            DefaultCredentialKey = "primary"
        }))
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://service.test/")
            .EnableTokenUnauthorizedRetry();

        using var response = await client.SendAsync(request, default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.RequestCount);
        Assert.Equal(2, provider.GetCount);
    }

    [Fact]
    public async Task NamedHttpClientsKeepTheirOwnTokenHandlerConfiguration()
    {
        var tokenProvider = new FakeTokenProvider("first-token", "second-token");
        var firstHandler = new RecordingHandler(HttpStatusCode.OK);
        var secondHandler = new RecordingHandler(HttpStatusCode.OK);
        var services = new ServiceCollection();
        services.AddSingleton<ITokenProvider>(tokenProvider);
        services.AddPubSoftTokens();
        services.AddHttpClient("first")
            .AddTokenHandler("first-key", "X-Access", "Token ")
            .ConfigurePrimaryHttpMessageHandler(() => firstHandler);
        services.AddHttpClient("second")
            .AddTokenHandler("second-key", "X-Access", "Bearer ")
            .ConfigurePrimaryHttpMessageHandler(() => secondHandler);
        using var serviceProvider = services.BuildServiceProvider();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using (var response = await clientFactory.CreateClient("first").GetAsync("https://service.test/first"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await clientFactory.CreateClient("second").GetAsync("https://service.test/second"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(["first-key", "second-key"], tokenProvider.RequestedKeys.ToArray());
        Assert.Equal("Token first-token", Assert.Single(firstHandler.AccessValues));
        Assert.Equal("Bearer second-token", Assert.Single(secondHandler.AccessValues));
    }

    [Fact]
    public void OptionsRejectUnsafeOrInvalidCredentialConfiguration()
    {
        var services = new ServiceCollection();
        services.AddPubSoftTokens(options => options.Credentials["bad"] = new TokenCredential
        {
            Url = "https://user:pass@auth.test/token"
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TokenManagerOptions>>().Value);
    }

    [Fact]
    public void OptionsRejectPlaintextHttpTokenEndpointForRemoteHost()
    {
        var services = new ServiceCollection();
        services.AddPubSoftTokens(options => options.Credentials["bad"] = new TokenCredential
        {
            Url = "http://auth.example.com/token"
        });
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TokenManagerOptions>>().Value);
    }

    [Fact]
    public void OptionsAllowPlaintextHttpTokenEndpointOnLoopback()
    {
        var services = new ServiceCollection();
        services.AddPubSoftTokens(options => options.Credentials["dev"] = new TokenCredential
        {
            Url = "http://localhost:8080/token"
        });
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOptions<TokenManagerOptions>>().Value);
    }

    [Fact]
    public void OptionsAllowPlaintextHttpTokenEndpointWithExplicitOptIn()
    {
        var services = new ServiceCollection();
        services.AddPubSoftTokens(options => options.Credentials["legacy"] = new TokenCredential
        {
            Url = "http://auth.example.com/token",
            AllowInsecureUrl = true
        });
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOptions<TokenManagerOptions>>().Value);
    }

    [Fact]
    public async Task ConfigureRequestRunsBeforeSend()
    {
        var handler = new CapturingHandler();
        var provider = CreateProvider(handler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token",
            ConfigureRequest = request => request.Headers.TryAddWithoutValidation("X-Client-Assertion", "signed-jwt"),
        });

        Assert.Equal("tok", await provider.GetTokenAsync("primary"));
        Assert.Equal("signed-jwt", handler.LastRequest!.Headers.GetValues("X-Client-Assertion").Single());
    }

    [Fact]
    public async Task OversizedTokenEndpointResponseIsRejected()
    {
        var huge = "{\"token\":\"tok\",\"pad\":\"" + new string('x', 70 * 1024) + "\"}";
        var handler = new QueueHandler(_ => Task.FromResult(JsonResponse(huge)));
        var provider = CreateProvider(handler, new RecordingCache(), new TokenCredential
        {
            Url = "https://auth.test/token",
        });

        Assert.Null(await provider.GetTokenAsync("primary"));
    }

    private static HttpTokenProvider CreateProvider(
        HttpMessageHandler handler,
        ICache cache,
        TokenCredential credential,
        TimeProvider? timeProvider = null,
        ILogger<HttpTokenProvider>? logger = null)
    {
        var options = new TokenManagerOptions();
        options.Credentials["primary"] = credential;
        return new HttpTokenProvider(
            new TestHttpClientFactory(handler),
            cache,
            new TestOptionsMonitor<TokenManagerOptions>(options),
            timeProvider,
            logger);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(JsonResponse("{\"token\":\"tok\"}"));
        }
    }

    private sealed class QueueHandler(Func<CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return responseFactory(cancellationToken);
        }
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
        {
            var entry = formatter(state, exception);
            if (exception is not null)
                entry += Environment.NewLine + exception;
            Entries.Enqueue(entry);
        }
    }

    private sealed class RecordingCache : ICache
    {
        private readonly ConcurrentDictionary<string, object> _items = new(StringComparer.Ordinal);
        public TimeSpan? LastExpiry { get; private set; }
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
            => Task.FromResult(_items.TryGetValue(key, out var item) ? item as T : null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
        {
            LastExpiry = expiry;
            _items[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _items.TryRemove(key, out _);
            return Task.CompletedTask;
        }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.ContainsKey(key));
        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
            => throw new NotSupportedException();
        public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
            => throw new NotSupportedException();
        public Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
            => throw new NotSupportedException();
        public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeTokenProvider(params string?[] tokens) : ITokenProvider
    {
        private int _getCount;
        private int _tokenIndex;
        public int GetCount => Volatile.Read(ref _getCount);
        public int InvalidationCount { get; private set; }
        public ConcurrentQueue<string> RequestedKeys { get; } = [];

        public Task<string?> GetTokenAsync(string credentialKey, CancellationToken cancellationToken = default)
        {
            RequestedKeys.Enqueue(credentialKey);
            var index = Interlocked.Increment(ref _tokenIndex) - 1;
            Interlocked.Increment(ref _getCount);
            return Task.FromResult<string?>(tokens[Math.Min(index, tokens.Length - 1)]);
        }

        public Task InvalidateAsync(string credentialKey, CancellationToken cancellationToken = default)
        {
            InvalidationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public List<string?> AuthorizationValues { get; } = [];
        public List<string?> AccessValues { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _requestCount) - 1;
            AuthorizationValues.Add(request.Headers.Authorization?.ToString());
            AccessValues.Add(request.Headers.TryGetValues("X-Access", out var values) ? values.Single() : null);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(statuses[Math.Min(index, statuses.Length - 1)]);
        }
    }
}