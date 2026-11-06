using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;

/// <summary>
/// Edge rate-limit options — partitioned by clientId + scheme + method.
///
/// Three layers:
/// 1. Total limit — all requests counted, guards overall load
/// 2. Per-clientId limit — guards a single third-party client
/// 3. Failure limit — only failed requests counted (auth failures/handler errors), blocks signature probing
///
/// The path is not hard-coded; it follows the UseEdgeGateway path.
/// </summary>
public sealed class EdgeRateLimitOptions
{
    public const string SectionName = "Edge:RateLimit";

    /// <summary>Whether rate limiting is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Fallback per-clientId total limit (requests per window).</summary>
    public int ClientPermitLimit { get; set; } = 1000;

    /// <summary>Total-limit window (seconds).</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Total-limit queue length (0 = reject immediately when exceeded).</summary>
    public int QueueLimit { get; set; } = 0;

    // ── per-scheme quotas ──

    /// <summary>Hmac-scheme limit (B2B, per clientId).</summary>
    public int HmacPermitLimit { get; set; } = 500;

    /// <summary>Session-scheme limit (C-end, per sessionKey).</summary>
    public int SessionPermitLimit { get; set; } = 2000;

    /// <summary>None-scheme limit (anonymous/login endpoints, per IP — strict anti-brute-force).</summary>
    public int NonePermitLimit { get; set; } = 20;

    /// <summary>Md5-scheme limit (legacy clients, per clientId).</summary>
    public int Md5PermitLimit { get; set; } = 500;

    // ── failure limiting (blocks signature probing / brute force) ──

    /// <summary>Whether failure limiting is enabled.</summary>
    public bool FailureRateLimitEnabled { get; set; } = true;

    /// <summary>Failure limit (per clientId + IP).</summary>
    public int FailurePermitLimit { get; set; } = 30;

    /// <summary>Failure-limit window (seconds).</summary>
    public int FailureWindowSeconds { get; set; } = 60;

    /// <summary>None-scheme failure limit (login-endpoint anti-brute-force, per IP).</summary>
    public int NoneFailurePermitLimit { get; set; } = 10;
}

/// <summary>
/// Rate counter.
/// </summary>
internal sealed class EdgeRateCounter
{
    public int Count { get; set; }
    public DateTime ResetAt { get; set; }

    public bool IsExceeded(int limit) => Count >= limit;

    public EdgeRateCounter Increment() => new() { Count = Count + 1, ResetAt = ResetAt };
}

/// <summary>
/// Edge rate-limit middleware — install before <see cref="EdgeGatewayMiddleware"/>.
///
/// Partitions by clientId + scheme + method:
/// - total limit: clientId + scheme, returns a rate-limit response when exceeded
/// - failure limit: counts only auth failures/handler errors, blocks probing
///
/// The path is supplied to the middleware and must match UseEdgeGateway.
/// </summary>
public sealed class EdgeRateLimitMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly string _path;
    private readonly EdgeRateLimitOptions _options;
    private readonly bool _pseudoPathsEnabled;
    private readonly ConcurrentDictionary<string, EdgeRateCounter> _totalCounters = new();
    private readonly ConcurrentDictionary<string, EdgeRateCounter> _failureCounters = new();
    private readonly ILogger<EdgeRateLimitMiddleware> _logger;
    private readonly Timer _cleanupTimer;

    public EdgeRateLimitMiddleware(
        RequestDelegate next,
        string path,
        IOptions<EdgeRateLimitOptions> options,
        ILogger<EdgeRateLimitMiddleware> logger,
        IOptions<EdgeGatewayOptions>? gatewayOptions = null, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _next = next;
        _path = path.TrimEnd('/');
        _options = options.Value;
        _pseudoPathsEnabled = gatewayOptions?.Value.EnablePseudoPaths ?? false;
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !EdgePathMatcher.TryMatch(context.Request.Path.Value, _path, _pseudoPathsEnabled, out var pseudoMethod))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!_options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Peek at the envelope without consuming the body (buffer then rewind).
        EdgeRpcRequest? request = null;
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            context.Request.EnableBuffering();
            try
            {
                request = await System.Text.Json.JsonSerializer.DeserializeAsync<EdgeRpcRequest>(
                    context.Request.Body, EdgeJsonOptions.Default, context.RequestAborted).ConfigureAwait(false);
            }
            catch
            {
                // Parse failures are left to the gateway middleware.
            }
            context.Request.Body.Position = 0;
        }

        var clientId = request?.ClientId ?? "unknown";
        var scheme = request?.Scheme ?? EdgeAuthScheme.Hmac;
        var remoteIp = context.Connection.RemoteIpAddress;
        var ip = remoteIp == null ? "unknown"
            : remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4().ToString()
            : remoteIp.ToString();

        // ── 1. failure-limit pre-check ──
        var failKey = BuildFailureKey(clientId, scheme, ip);
        if (_options.FailureRateLimitEnabled &&
            _failureCounters.TryGetValue(failKey, out var failCounter) &&
            failCounter.IsExceeded(GetFailureLimit(scheme, _options)))
        {
            _logger.LogWarning("[EdgeRateLimit] failure limit hit key={Key}", failKey);
            await WriteLimitedAsync(context, request, EdgeErrorCodes.FailureRateLimited,
                "Too many failed requests, try again later",
                (int)Math.Max(1, (failCounter.ResetAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds)).ConfigureAwait(false);
            return;
        }

        // ── 2. total-limit pre-check ──
        var totalKey = BuildTotalKey(clientId, scheme, pseudoMethod ?? request?.Method, ip);
        var permitLimit = GetPermitLimit(scheme, _options);
        var window = TimeSpan.FromSeconds(Math.Max(1, _options.WindowSeconds));

        if (!_totalCounters.TryGetValue(totalKey, out var totalCounter))
        {
            totalCounter = new EdgeRateCounter { Count = 0, ResetAt = _timeProvider.GetUtcNow().UtcDateTime.Add(window) };
            _totalCounters[totalKey] = totalCounter;
        }

        if (_timeProvider.GetUtcNow().UtcDateTime >= totalCounter.ResetAt)
        {
            totalCounter.Count = 0;
            totalCounter.ResetAt = _timeProvider.GetUtcNow().UtcDateTime.Add(window);
        }

        if (totalCounter.IsExceeded(permitLimit))
        {
            _logger.LogWarning("[EdgeRateLimit] total limit hit key={Key} count={Count} limit={Limit}",
                totalKey, totalCounter.Count, permitLimit);
            await WriteLimitedAsync(context, request, EdgeErrorCodes.RateLimited,
                "Too many requests, try again later",
                (int)Math.Max(1, (totalCounter.ResetAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds)).ConfigureAwait(false);
            return;
        }

        totalCounter.Count++;

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await _next(context).ConfigureAwait(false);

            // Failure counting — error codes live in the body (HTTP is always 200).
            if (_options.FailureRateLimitEnabled)
            {
                memoryStream.Position = 0;
                EdgeRpcResponse? rpcResponse = null;
                try
                {
                    rpcResponse = await System.Text.Json.JsonSerializer.DeserializeAsync<EdgeRpcResponse>(
                        memoryStream, EdgeJsonOptions.Default, context.RequestAborted).ConfigureAwait(false);
                }
                catch { /* non-edge response — not counted */ }

                if (rpcResponse is { Ok: false } && !string.Equals(rpcResponse.Code, EdgeErrorCodes.RateLimited, System.StringComparison.Ordinal) && !string.Equals(rpcResponse.Code, EdgeErrorCodes.FailureRateLimited, System.StringComparison.Ordinal))
                {
                    RecordFailure(failKey, TimeSpan.FromSeconds(Math.Max(1, GetFailureWindow(scheme, _options))));
                }
            }
        }
        catch
        {
            if (_options.FailureRateLimitEnabled)
            {
                RecordFailure(failKey, TimeSpan.FromSeconds(Math.Max(1, GetFailureWindow(scheme, _options))));
            }
            throw;
        }
        finally
        {
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBodyStream, context.RequestAborted).ConfigureAwait(false);
            context.Response.Body = originalBodyStream;
        }
    }

    private static async Task WriteLimitedAsync(HttpContext context, EdgeRpcRequest? request, string code, string message, int retryAfter)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var response = EdgeRpcResponse.Error(code, message);
        if (request is not null)
            response = response.WithDiagnostics(request);
        await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, response, EdgeJsonOptions.Default, context.RequestAborted).ConfigureAwait(false);
    }

    private static string BuildTotalKey(string clientId, EdgeAuthScheme scheme, string? method, string ip)
    {
        // None scheme is rate-limited per IP (no clientId auth).
        if (scheme == EdgeAuthScheme.None)
            return $"total:none:{ip}";

        return $"total:{clientId}:{scheme}:{method ?? "any"}";
    }

    private static string BuildFailureKey(string clientId, EdgeAuthScheme scheme, string ip)
    {
        if (scheme == EdgeAuthScheme.None)
            return $"fail:none:{ip}";

        return $"fail:{clientId}:{scheme}:{ip}";
    }

    private static int GetPermitLimit(EdgeAuthScheme scheme, EdgeRateLimitOptions options) => scheme switch
    {
        EdgeAuthScheme.Hmac => options.HmacPermitLimit,
        EdgeAuthScheme.Session => options.SessionPermitLimit,
        EdgeAuthScheme.Md5 => options.Md5PermitLimit,
        EdgeAuthScheme.None => options.NonePermitLimit,
        _ => options.ClientPermitLimit,
    };

    private static int GetFailureLimit(EdgeAuthScheme scheme, EdgeRateLimitOptions options) => scheme switch
    {
        EdgeAuthScheme.None => options.NoneFailurePermitLimit,
        _ => options.FailurePermitLimit,
    };

    private static int GetFailureWindow(EdgeAuthScheme scheme, EdgeRateLimitOptions options) => scheme switch
    {
        EdgeAuthScheme.None => Math.Max(1, options.FailureWindowSeconds),
        _ => options.FailureWindowSeconds,
    };

    private void RecordFailure(string key, TimeSpan window)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _failureCounters.AddOrUpdate(
            key,
            _ => new EdgeRateCounter { Count = 1, ResetAt = now.Add(window) },
            (_, existing) =>
            {
                if (now >= existing.ResetAt)
                    return new EdgeRateCounter { Count = 1, ResetAt = now.Add(window) };
                return existing.Increment();
            });

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[EdgeRateLimit] RecordFailure key={Key} count={Count}", key, _failureCounters[key].Count);
        }
    }

    private void CleanupExpired(object? state)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var kv in _totalCounters.Where(x => x.Value.ResetAt < now).ToList())
            _totalCounters.TryRemove(kv.Key, out _);

        foreach (var kv in _failureCounters.Where(x => x.Value.ResetAt < now).ToList())
            _failureCounters.TryRemove(kv.Key, out _);
    }

    public void Dispose() => _cleanupTimer.Dispose();
}

/// <summary>
/// Edge rate-limit DI extensions.
/// </summary>
public static class EdgeRateLimitExtensions
{
    /// <summary>Registers Edge rate limiting (options from configuration).</summary>
    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EdgeRateLimitOptions>(configuration.GetSection(EdgeRateLimitOptions.SectionName));
        return services;
    }

    /// <summary>Registers Edge rate limiting (explicit configuration).</summary>
    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services,
        Action<EdgeRateLimitOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Installs the rate-limit middleware — path must match UseEdgeGateway.
    /// <code>
    /// app.UseEdgeRateLimiting("/rpc");
    /// app.UseEdgeGateway("/rpc");
    /// </code>
    /// </summary>
    public static IApplicationBuilder UseEdgeRateLimiting(this IApplicationBuilder app, string path)
    {
        return app.UseMiddleware<EdgeRateLimitMiddleware>(path);
    }
}
