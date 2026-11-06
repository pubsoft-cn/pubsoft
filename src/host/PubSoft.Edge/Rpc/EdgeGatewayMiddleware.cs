using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;


/// <summary>
/// Edge RPC gateway middleware -- single entry point dispatching <c>method</c> to <see cref="IEdgeHandler"/>.
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

    /// <summary>Creates the middleware.</summary>
    public EdgeGatewayMiddleware(RequestDelegate next, string path, IOptions<EdgeGatewayOptions>? options = null)
    {
        _next = next;
        _path = path.TrimEnd('/');
        _options = options?.Value ?? new EdgeGatewayOptions();
    }

    /// <summary>Routes the request through the edge pipeline.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !EdgePathMatcher.TryMatch(context.Request.Path.Value, _path, _options.EnablePseudoPaths, out var pseudoMethod))
        {
            await _next(context).ConfigureAwait(false);
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
                context.Request.Body, EdgeJsonOptions.Default, context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await WriteResponseAsync(context, null, EdgeError.BadRequest($"Request parsing failed: {ex.Message}")).ConfigureAwait(false);
            return;
        }

        // Pseudo-path: the path tail is authoritative for dispatch (doc and wire agree).
        if (request is not null && pseudoMethod is not null)
            request.Method = pseudoMethod;

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            await WriteResponseAsync(context, request, EdgeError.BadRequest("method is required")).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.ClientIdRequired, "clientId is required")).ConfigureAwait(false);
            return;
        }

        // Timestamp tolerance - signature schemes only, replay hardening.
        if (_options.TimestampToleranceSeconds > 0
            && request.Scheme is EdgeAuthScheme.Hmac or EdgeAuthScheme.Md5)
        {
            var skew = Math.Abs(TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds() - request.Timestamp);
            if (skew > _options.TimestampToleranceSeconds * 1000L)
            {
                await WriteResponseAsync(context, request,
                    EdgeRpcResponse.Error(EdgeErrorCodes.TimestampOutOfRange, "timestamp outside accepted window")).ConfigureAwait(false);
                return;
            }
        }

        if (!handlerRegistry.TryGetHandler(request.Method, out var handler))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.MethodNotFound, $"method not found: {request.Method}")).ConfigureAwait(false);
            return;
        }

        // Per-client scheme pinning (registered clients cannot downgrade to a weaker scheme).
        var schemePolicy = context.RequestServices.GetService<IEdgeSchemePolicy>();
        var allowedScheme = schemePolicy?.GetAllowedScheme(request.ClientId);
        if (allowedScheme.HasValue && request.Scheme != allowedScheme.Value)
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SchemeNotAccepted,
                $"clientId={request.ClientId} only accepts scheme={allowedScheme.Value}")).ConfigureAwait(false);
            return;
        }

        var handlerSchemes = GetHandlerSchemes(handler);
        if (!handlerSchemes.Contains(request.Scheme))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SchemeNotAccepted,
                $"method does not accept scheme={request.Scheme}")).ConfigureAwait(false);
            return;
        }

        // Authentication
        EdgeContext edgeContext;
        try
        {
            edgeContext = await AuthenticateAsync(context, request, signKeyProvider, logger).ConfigureAwait(false);
            if (!edgeContext.IsVerified && request.Scheme != EdgeAuthScheme.None)
            {
                // Failure response was already written by AuthenticateAsync.
                return;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Edge authentication error: {Method}", request.Method);
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.AuthError, "Authentication error")).ConfigureAwait(false);
            return;
        }

        // Dispatch
        try
        {
            var response = await handler.ExecuteAsync(request, edgeContext, context.RequestAborted).ConfigureAwait(false);
            await WriteResponseAsync(context, request, response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Edge handler error: {Method}", request.Method);
            await WriteResponseAsync(context, request, EdgeError.InternalError("Internal error")).ConfigureAwait(false);
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
                return await AuthenticateSignatureAsync(context, request, signKeyProvider, baseContext, logger).ConfigureAwait(false);

            case EdgeAuthScheme.Session:
                return await AuthenticateSessionAsync(context, request, baseContext, logger).ConfigureAwait(false);

            default:
                await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.UnknownScheme, $"Unknown scheme: {request.Scheme}")).ConfigureAwait(false);
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
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SignServiceNotConfigured, "Signing service not configured")).ConfigureAwait(false);
            return baseContext with { IsVerified = false };
        }

        var secretKey = signKeyProvider.GetSecretKey(request.ClientId);
        if (string.IsNullOrEmpty(secretKey))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.ClientNotConfigured, $"clientId not configured: {request.ClientId}")).ConfigureAwait(false);
            return baseContext with { IsVerified = false };
        }

        var result = EdgeSigner.Verify(request, secretKey);
        if (!result.Success)
        {
            var msg = signKeyProvider.ShowSignDebug && result.SignPayload is not null
                ? $"{result.ErrorMessage}, signing payload: {result.SignPayload}"
                : result.ErrorMessage ?? "Signature verification failed";
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SignatureInvalid, msg)).ConfigureAwait(false);
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
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SessionServiceNotConfigured, "Session service not configured")).ConfigureAwait(false);
            return baseContext with { IsVerified = false };
        }

        if (string.IsNullOrEmpty(request.Credential))
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SessionKeyRequired, "credential (sessionKey) is required")).ConfigureAwait(false);
            return baseContext with { IsVerified = false };
        }

        var session = await sessionManager.ValidateAsync(request.Credential, ct: context.RequestAborted).ConfigureAwait(false);
        if (session is null)
        {
            await WriteResponseAsync(context, request, EdgeRpcResponse.Error(EdgeErrorCodes.SessionKeyInvalid, "sessionKey expired or does not exist")).ConfigureAwait(false);
            return baseContext with { IsVerified = false };
        }

        return baseContext with { IsVerified = true, Session = session };
    }

    /// <summary>Handler-accepted schemes -- attribute wins over the interface property.</summary>
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
        await JsonSerializer.SerializeAsync(context.Response.Body, response, EdgeJsonOptions.Default, context.RequestAborted).ConfigureAwait(false);
    }
}
