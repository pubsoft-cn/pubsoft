using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Edge;

/// <summary>
/// Edge RPC client — used by callers, supports all credential schemes.
///
/// Usage:
/// <code>
/// // Hmac (B2B signing)
/// var client = new EdgeClient(http, "my-client", "my-secret");
/// var result = await client.InvokeAsync&lt;MyDto&gt;("demo.ping", new { itemId = 1 });
///
/// // Session (C-end, sessionKey from login)
/// var client = new EdgeClient(http, "my-client", sessionKey: "client:app:guid");
///
/// // None (anonymous, e.g. login endpoints)
/// var client = new EdgeClient(http, "my-client", EdgeAuthScheme.None);
/// </code>
/// </summary>
public sealed class EdgeClient
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly EdgeAuthScheme _scheme;
    private readonly string? _secretKey;
    private readonly string? _sessionKey;
    private readonly string _path;

    /// <summary>Creates a signature-scheme client (Hmac default, or Md5 for legacy).</summary>
    public EdgeClient(HttpClient http, string clientId, string secretKey, EdgeAuthScheme scheme = EdgeAuthScheme.Hmac, string path = "/rpc")
    {
        _http = http;
        _clientId = clientId;
        _scheme = scheme;
        _secretKey = secretKey;
        _sessionKey = null;
        _path = path;
    }

    /// <summary>Creates a Session-scheme client (C-end).</summary>
    public EdgeClient(HttpClient http, string clientId, string sessionKey, string path)
    {
        _http = http;
        _clientId = clientId;
        _scheme = EdgeAuthScheme.Session;
        _secretKey = null;
        _sessionKey = sessionKey;
        _path = path;
    }

    /// <summary>Creates a Session-scheme client (C-end), default /rpc path.</summary>
    public EdgeClient(HttpClient http, string clientId, string sessionKey)
        : this(http, clientId, sessionKey, "/rpc") { }

    /// <summary>Creates a client with no credential scheme (anonymous).</summary>
    public EdgeClient(HttpClient http, string clientId, EdgeAuthScheme scheme = EdgeAuthScheme.None, string path = "/rpc")
    {
        _http = http;
        _clientId = clientId;
        _scheme = scheme;
        _secretKey = null;
        _sessionKey = null;
        _path = path;
    }

    /// <summary>Correlation id of the last request sent.</summary>
    public string? LastRequestId { get; private set; }

    /// <summary>Invokes an Edge RPC (auto-signs; throws on error responses).</summary>
    public async Task<T?> InvokeAsync<T>(string method, object? parameters, CancellationToken ct = default)
    {
        var rpcResponse = await SendAsync(method, parameters, ct).ConfigureAwait(false);
        if (rpcResponse.Data is null || !rpcResponse.Data.HasValue)
            return default;

        return rpcResponse.Data.Value.Deserialize<T>(EdgeJsonOptions.Default);
    }

    /// <summary>Invokes an Edge RPC without a return value.</summary>
    public async Task InvokeVoidAsync(string method, object? parameters, CancellationToken ct = default)
    {
        await SendAsync(method, parameters, ct).ConfigureAwait(false);
    }

    /// <summary>Builds a signed request without sending (debug/testing).</summary>
    public EdgeRpcRequest BuildRequest(string method, object? parameters)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        if (_scheme == EdgeAuthScheme.Session)
        {
            return new EdgeRpcRequest
            {
                ClientId = _clientId,
                Method = method,
                Scheme = EdgeAuthScheme.Session,
                Credential = _sessionKey!,
                Timestamp = timestamp,
                RequestId = requestId,
                Params = ToParamsElement(parameters),
            };
        }

        if (_scheme == EdgeAuthScheme.None)
        {
            return new EdgeRpcRequest
            {
                ClientId = _clientId,
                Method = method,
                Scheme = EdgeAuthScheme.None,
                Timestamp = timestamp,
                RequestId = requestId,
                Params = ToParamsElement(parameters),
            };
        }

        var nonce = Guid.NewGuid().ToString("N");
        var credential = _scheme == EdgeAuthScheme.Md5
            ? EdgeSigner.SignMd5(method, timestamp, nonce, requestId, parameters, _secretKey!)
            : EdgeSigner.SignHmac(method, timestamp, nonce, requestId, parameters, _secretKey!);

        return new EdgeRpcRequest
        {
            ClientId = _clientId,
            Method = method,
            Scheme = _scheme,
            Credential = credential,
            Timestamp = timestamp,
            Nonce = nonce,
            RequestId = requestId,
            Params = ToParamsElement(parameters),
        };
    }

    /// <summary>Returns the canonical signing payload for debugging.</summary>
    public string? GetSignPayload(string method, object? parameters)
    {
        return _scheme is EdgeAuthScheme.Hmac or EdgeAuthScheme.Md5
            ? EdgeSigner.BuildSignPayload(method, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), parameters)
            : null;
    }

    private async Task<EdgeRpcResponse> SendAsync(string method, object? parameters, CancellationToken ct)
    {
        var request = BuildRequest(method, parameters);
        LastRequestId = request.RequestId;

        using var response = await _http.PostAsJsonAsync(_path, request, EdgeJsonOptions.Default, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new EdgeRpcException(((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), $"HTTP {response.StatusCode}", method);

        var rpcResponse = await response.Content.ReadFromJsonAsync<EdgeRpcResponse>(EdgeJsonOptions.Default, ct).ConfigureAwait(false);
        if (rpcResponse is null || !rpcResponse.Ok)
            throw new EdgeRpcException(rpcResponse?.Code ?? "0", rpcResponse?.Message ?? "Unknown error", method);

        return rpcResponse;
    }

    private static JsonElement ToParamsElement(object? parameters)
        => parameters is null
            ? JsonSerializer.SerializeToElement(new { }, EdgeJsonOptions.Default)
            : JsonSerializer.SerializeToElement(parameters, EdgeJsonOptions.Default);
}

/// <summary>
/// Edge RPC exception — thrown client-side on error responses.
/// </summary>
public class EdgeRpcException(string code, string message, string method) : Exception($"[{code}] {method}: {message}")
{
    public string Code { get; } = code;
    public string Method { get; } = method;
}

/// <summary>
/// DI extensions for <see cref="EdgeClient"/>.
/// </summary>
public static class EdgeClientExtensions
{
    /// <summary>Registers a signature-scheme EdgeClient (Hmac default, or Md5).</summary>
    public static IServiceCollection AddEdgeClient(
        this IServiceCollection services,
        string baseUrl,
        string clientId,
        string secretKey,
        TimeSpan? timeout = null,
        EdgeAuthScheme scheme = EdgeAuthScheme.Hmac)
    {
        services.AddHttpClient<EdgeClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        });
        services.AddSingleton(new EdgeClientOptions(clientId, secretKey, null, scheme));
        services.AddTransient<EdgeClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(EdgeClient));
            var opts = sp.GetRequiredService<EdgeClientOptions>();
            return new EdgeClient(http, opts.ClientId, opts.SecretKey!, opts.Scheme);
        });
        return services;
    }

    /// <summary>Registers a Session-scheme EdgeClient (C-end).</summary>
    public static IServiceCollection AddEdgeClientWithSession(
        this IServiceCollection services,
        string baseUrl,
        string clientId,
        string sessionKey,
        TimeSpan? timeout = null)
    {
        services.AddHttpClient<EdgeClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        });
        services.AddSingleton(new EdgeClientOptions(clientId, null, sessionKey, EdgeAuthScheme.Session));
        services.AddTransient<EdgeClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(EdgeClient));
            var opts = sp.GetRequiredService<EdgeClientOptions>();
            return new EdgeClient(http, opts.ClientId, opts.SessionKey!);
        });
        return services;
    }
}

internal sealed record EdgeClientOptions(string ClientId, string? SecretKey, string? SessionKey, EdgeAuthScheme Scheme);
