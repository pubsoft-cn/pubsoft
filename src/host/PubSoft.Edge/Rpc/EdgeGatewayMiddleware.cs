using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;

/// <summary>
/// Edge gateway options.
/// </summary>
public sealed class EdgeGatewayOptions
{
    public const string SectionName = "Edge:Gateway";

    /// <summary>
    /// Accepted clock skew for signed requests (seconds). 0 disables the check.
    /// Signature schemes (Hmac/Md5) are rejected when |now - timestamp| exceeds this window.
    /// </summary>
    public int TimestampToleranceSeconds { get; set; } = 300;
}

/// <summary>
/// Edge RPC gateway middleware — single entry point dispatching <c>method</c> to <see cref="IEdgeHandler"/>.
///
/// Pipeline:
/// 1. Parse the request envelope, read scheme (default Hmac)
/// 2. Resolve the handler, check it accepts the scheme
/// 3. Authenticate per scheme:
///    - Hmac/Md5: EdgeSigner.Verify (canonical signing string)
///    - Session:  EdgeSessionManager.ValidateAsync
///    - None:     skip authentication
/// 4. Invoke the handler; response carries requestId/traceId diagnostics.
/// </summary>
public sealed class EdgeGatewayMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _path;
    private readonly EdgeGatewayOptions _options;

    public EdgeGatewayMiddleware(RequestDelegate next, string path, IOptions<EdgeGatewayOptions>? options = null)
    {
        _next = next;
        _path = path.TrimEnd('/');
        _options = options?.Value ?? new EdgeGatewayOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;

        if (!path.Equals(_path, StringComparison.OrdinalIgnoreCase) || !HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var logger = context.RequestServices.GetService<ILogger<EdgeGatewayMiddleware>>();
        var handlerRegistry = context.RequestServices.GetRequiredService<EdgeHandlerRegistry>();
        var signKeyProvider = context.RequestServices.GetService<IEdgeSignKeyProvider>();

        EdgeRpcRequest? request;
        try
        {
            context.Request.EnableBuffering();
            request = await JsonSerializer.DeserializeAsync<EdgeRpcRequest>(
                context.Request.Body, EdgeJsonOptions.Default, context.RequestAborted);
        }
        catch (JsonException ex)
        {
            await WriteResponseAsync(context, null, EdgeError.BadRequest($"Request parsing failed: {ex.Message}"));
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            await WriteResponseAsync(context, request, EdgeError.BadRequest("method is required"));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.ClientIdRequired, "clientId is required"));
            return;
        }

        // Timestamp tolerance — signature schemes only, replay hardening.
        if (_options.TimestampToleranceSeconds > 0
            && request.Scheme is EdgeAuthScheme.Hmac or EdgeAuthScheme.Md5)
        {
            var skew = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - request.Timestamp);
            if (skew > _options.TimestampToleranceSeconds * 1000L)
            {
                await WriteResponseAsync(context, request,
                    EdgeRpcResponse.Error(EdgeErrorCodes.TimestampOutOfRange, "timestamp outside accepted window"));
                return;
            }
        }

        if (!handlerRegistry.TryGetHandler(request.Method, out var handler))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.MethodNotFound, $"method not found: {request.Method}"));
            return;
        }

        // Per-client scheme pinning (registered clients cannot downgrade to a weaker scheme).
        var schemePolicy = context.RequestServices.GetService<IEdgeSchemePolicy>();
        var allowedScheme = schemePolicy?.GetAllowedScheme(request.ClientId);
        if (allowedScheme.HasValue && request.Scheme != allowedScheme.Value)
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SchemeNotAccepted,
                $"clientId={request.ClientId} only accepts scheme={allowedScheme.Value}"));
            return;
        }

        var handlerSchemes = GetHandlerSchemes(handler);
        if (!handlerSchemes.Contains(request.Scheme) && request.Scheme != EdgeAuthScheme.None)
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SchemeNotAccepted,
                $"method does not accept scheme={request.Scheme}"));
            return;
        }

        // Authentication
        EdgeContext edgeContext;
        try
        {
            edgeContext = await AuthenticateAsync(context, request, signKeyProvider, logger);
            if (!edgeContext.IsVerified && request.Scheme != EdgeAuthScheme.None)
            {
                // Failure response was already written by AuthenticateAsync.
                return;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Edge authentication error: {Method}", request.Method);
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.AuthError, "Authentication error"));
            return;
        }

        // Dispatch
        try
        {
            var response = await handler.ExecuteAsync(request, edgeContext, context.RequestAborted);
            await WriteResponseAsync(context, request, response);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Edge handler error: {Method}", request.Method);
            await WriteResponseAsync(context, request, EdgeError.InternalError("Internal error"));
        }
    }

    private static async Task<EdgeContext> AuthenticateAsync(
        HttpContext context, EdgeRpcRequest request,
        IEdgeSignKeyProvider? signKeyProvider, ILogger? logger)
    {
        var baseContext = new EdgeContext
        {
            HttpContext = context,
            Scheme = request.Scheme,
            ClientId = request.ClientId,
            RequestId = request.RequestId,
        };

        switch (request.Scheme)
        {
            case EdgeAuthScheme.None:
                return baseContext with { IsVerified = true };

            case EdgeAuthScheme.Hmac:
            case EdgeAuthScheme.Md5:
                return await AuthenticateSignatureAsync(context, request, signKeyProvider, baseContext, logger);

            case EdgeAuthScheme.Session:
                return await AuthenticateSessionAsync(context, request, baseContext, logger);

            default:
                await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.UnknownScheme, $"Unknown scheme: {request.Scheme}"));
                return baseContext with { IsVerified = false };
        }
    }

    private static async Task<EdgeContext> AuthenticateSignatureAsync(
        HttpContext context, EdgeRpcRequest request,
        IEdgeSignKeyProvider? signKeyProvider, EdgeContext baseContext, ILogger? logger)
    {
        if (signKeyProvider is null)
        {
            logger?.LogError("IEdgeSignKeyProvider not registered");
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SignServiceNotConfigured, "Signing service not configured"));
            return baseContext with { IsVerified = false };
        }

        var secretKey = signKeyProvider.GetSecretKey(request.ClientId);
        if (string.IsNullOrEmpty(secretKey))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.ClientNotConfigured, $"clientId not configured: {request.ClientId}"));
            return baseContext with { IsVerified = false };
        }

        var result = EdgeSigner.Verify(request, secretKey);
        if (!result.Success)
        {
            var msg = signKeyProvider.ShowSignDebug && result.SignPayload is not null
                ? $"{result.ErrorMessage}, signing payload: {result.SignPayload}"
                : result.ErrorMessage ?? "Signature verification failed";
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SignatureInvalid, msg));
            return baseContext with { IsVerified = false };
        }

        return baseContext with { IsVerified = true };
    }

    private static async Task<EdgeContext> AuthenticateSessionAsync(
        HttpContext context, EdgeRpcRequest request, EdgeContext baseContext, ILogger? logger)
    {
        var sessionManager = context.RequestServices.GetService<EdgeSessionManager>();
        if (sessionManager is null)
        {
            logger?.LogError("EdgeSessionManager not registered");
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SessionServiceNotConfigured, "Session service not configured"));
            return baseContext with { IsVerified = false };
        }

        if (string.IsNullOrEmpty(request.Credential))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SessionKeyRequired, "credential (sessionKey) is required"));
            return baseContext with { IsVerified = false };
        }

        var session = await sessionManager.ValidateAsync(request.Credential, ct: context.RequestAborted);
        if (session is null)
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SessionKeyInvalid, "sessionKey expired or does not exist"));
            return baseContext with { IsVerified = false };
        }

        return baseContext with { IsVerified = true, Session = session };
    }

    /// <summary>Handler-accepted schemes — attribute wins over the interface property.</summary>
    private static EdgeAuthScheme[] GetHandlerSchemes(IEdgeHandler handler)
    {
        return handler.GetType().GetCustomAttribute<EdgeSchemeAttribute>()?.Schemes ?? handler.Schemes;
    }

    private static async Task WriteResponseAsync(HttpContext context, EdgeRpcRequest? request, EdgeRpcResponse response)
    {
        if (context.Response.HasStarted)
            return;

        if (request is not null)
            response = response.WithDiagnostics(request);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = 200;
        await JsonSerializer.SerializeAsync(context.Response.Body, response, EdgeJsonOptions.Default, context.RequestAborted);
    }
}

/// <summary>
/// Optional per-clientId scheme pinning — when registered, a client may only use the declared
/// scheme (downgrade protection). Null = unrestricted.
/// </summary>
public interface IEdgeSchemePolicy
{
    /// <summary>Returns the pinned scheme for a clientId, or null to allow any scheme.</summary>
    EdgeAuthScheme? GetAllowedScheme(string clientId);
}

/// <summary>
/// Signing-key provider — implemented by the host; returns the secret key for a clientId.
/// </summary>
public interface IEdgeSignKeyProvider
{
    /// <summary>Gets the signing secret for a clientId.</summary>
    string? GetSecretKey(string clientId);

    /// <summary>Expose the signing payload in error responses (debug only; keep false in production).</summary>
    bool ShowSignDebug { get; }
}

/// <summary>
/// Edge handler registry — DI-backed method → handler map.
/// </summary>
public sealed class EdgeHandlerRegistry
{
    private readonly Dictionary<string, IEdgeHandler> _handlers = new(StringComparer.Ordinal);

    internal void Register(IEdgeHandler handler)
    {
        _handlers[handler.Method] = handler;
    }

    public bool TryGetHandler(string method, out IEdgeHandler handler)
    {
        return _handlers.TryGetValue(method, out handler!);
    }
}

/// <summary>
/// DI extensions for the Edge gateway.
/// </summary>
public static class EdgeGatewayExtensions
{
    /// <summary>Registers an Edge handler (auto-added to the registry).</summary>
    public static IServiceCollection AddEdgeHandler<THandler>(this IServiceCollection services)
        where THandler : class, IEdgeHandler
    {
        services.AddTransient<THandler>();
        services.AddSingleton<IEdgeHandler>(sp => sp.GetRequiredService<THandler>());
        return services;
    }

    /// <summary>Registers the Edge gateway infrastructure.</summary>
    public static IServiceCollection AddEdgeGateway(this IServiceCollection services)
    {
        services.AddSingleton<EdgeHandlerRegistry>(sp =>
        {
            var registry = new EdgeHandlerRegistry();
            foreach (var handler in sp.GetServices<IEdgeHandler>())
                registry.Register(handler);
            return registry;
        });
        return services;
    }

    /// <summary>Registers the Edge gateway infrastructure with explicit options.</summary>
    public static IServiceCollection AddEdgeGateway(this IServiceCollection services, Action<EdgeGatewayOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<EdgeHandlerRegistry>(sp =>
        {
            var registry = new EdgeHandlerRegistry();
            foreach (var handler in sp.GetServices<IEdgeHandler>())
                registry.Register(handler);
            return registry;
        });
        return services;
    }

    /// <summary>Binds gateway options from configuration.</summary>
    public static IServiceCollection AddEdgeGateway(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<EdgeGatewayOptions>(configuration.GetSection(EdgeGatewayOptions.SectionName));
        return AddEdgeGateway(services);
    }

    /// <summary>Installs the Edge gateway middleware.</summary>
    public static IApplicationBuilder UseEdgeGateway(this IApplicationBuilder app, string path)
    {
        return app.UseMiddleware<EdgeGatewayMiddleware>(path);
    }
}
