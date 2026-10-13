using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching;
using PubSoft.Tokens;

var services = new ServiceCollection();
services.AddPubSoftTokens(options => options.Credentials["service"] = new TokenCredential
{
    Url = "https://identity.example.test/connect/token",
    TokenPath = "access_token",
    Expiry = TimeSpan.FromMinutes(10),
    RefreshBefore = TimeSpan.FromMinutes(1)
});
services.AddSingleton<ICache, SmokeCache>();
services.AddHttpClient(HttpTokenProvider.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new TokenEndpointHandler());
var receivedAuthorization = string.Empty;
var serviceRequestCount = 0;
services.AddHttpClient("service-api")
    .AddTokenHandler("service")
    .ConfigurePrimaryHttpMessageHandler(() => new ProtectedServiceHandler(
        value => receivedAuthorization = value,
        () => Interlocked.Increment(ref serviceRequestCount) == 1
            ? HttpStatusCode.Unauthorized
            : HttpStatusCode.OK));
using var provider = services.BuildServiceProvider();

var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("service-api");
using var request = new HttpRequestMessage(HttpMethod.Get, "https://service.example.test/profile")
    .EnableTokenUnauthorizedRetry();
using var response = await client.SendAsync(request);
if (response.StatusCode != HttpStatusCode.OK
    || receivedAuthorization != "Bearer consumer-token"
    || serviceRequestCount != 2)
    throw new InvalidOperationException("Tokens package opt-in 401 replay consumer check failed.");

Console.WriteLine("Isolated PubSoft.Tokens opt-in 401 replay consumer check passed.");

sealed class TokenEndpointHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"consumer-token\"}", Encoding.UTF8, "application/json")
        });
    }
}

sealed class ProtectedServiceHandler(Action<string> captureAuthorization, Func<HttpStatusCode> responseStatus) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        captureAuthorization(request.Headers.Authorization?.ToString() ?? string.Empty);
        return Task.FromResult(new HttpResponseMessage(responseStatus()));
    }
}

sealed class SmokeCache : ICache
{
    private readonly ConcurrentDictionary<string, object> _items = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryGetValue(key, out var value) ? value as T : null);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.ContainsKey(key));

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where T : class
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var created = await factory();
        await SetAsync(key, created, expiry, cancellationToken);
        return created;
    }

    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        where T : class
    {
        var results = new Dictionary<string, T?>(StringComparer.Ordinal);
        foreach (var key in keys)
            results[key] = await GetAsync<T>(key, cancellationToken);
        return results;
    }

    public async Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        where T : class
    {
        foreach (var item in items)
            await SetAsync(item.Key, item.Value, expiry, cancellationToken);
    }

    public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
            _items.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        foreach (var key in _items.Keys.Where(key => key.Contains(pattern, StringComparison.Ordinal)))
            _items.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}