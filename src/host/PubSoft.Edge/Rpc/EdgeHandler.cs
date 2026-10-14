using System.Text.Json;

namespace PubSoft.Edge;

/// <summary>
/// Declares which credential schemes a handler accepts — attribute form, takes precedence over the property.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class EdgeSchemeAttribute(params EdgeAuthScheme[] schemes) : Attribute
{
    public EdgeAuthScheme[] Schemes { get; } = schemes;
}

/// <summary>
/// Edge RPC handler — one method maps to one handler.
/// </summary>
public interface IEdgeHandler
{
    /// <summary>RPC method string (e.g. "demo.ping").</summary>
    string Method { get; }

    /// <summary>
    /// Schemes this handler accepts. <see cref="EdgeHandlerBase{TParams}"/> provides a virtual
    /// default (Hmac + Session); the <see cref="EdgeSchemeAttribute"/> overrides this for any handler.
    /// </summary>
    EdgeAuthScheme[] Schemes { get; }

    /// <summary>Handles the request.</summary>
    Task<EdgeRpcResponse> ExecuteAsync(EdgeRpcRequest request, EdgeContext context, CancellationToken ct);
}

/// <summary>
/// Edge call context — authenticated session data plus the HttpContext.
/// </summary>
public sealed record EdgeContext
{
    /// <summary>HttpContext (null for non-HTTP invocations).</summary>
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; init; }

    /// <summary>Scheme used by this request.</summary>
    public EdgeAuthScheme Scheme { get; init; } = EdgeAuthScheme.None;

    /// <summary>Registered client system identifier.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Client-supplied correlation id, when present.</summary>
    public string? RequestId { get; init; }

    /// <summary>Credential verified flag.</summary>
    public bool IsVerified { get; init; }

    /// <summary>Session data (Session scheme; contains the backend token and user info).</summary>
    public EdgeSession? Session { get; init; }

    /// <summary>Current user id (Session scheme).</summary>
    public string? UserId => Session?.UserId;

    /// <summary>Current member id (Session scheme).</summary>
    public string? MemberId => Session?.MemberId;

    /// <summary>Current open id (Session scheme).</summary>
    public string? OpenId => Session?.OpenId;

    /// <summary>Current tenant id.</summary>
    public string? TenantId => Session?.TenantId;

    /// <summary>Session roles.</summary>
    public string[] Roles => Session?.Roles ?? [];

    /// <summary>Backend token held by the session (never exposed to the caller).</summary>
    public string? Token => Session?.Token;
}

/// <summary>
/// Edge RPC handler base — strongly-typed params deserialization + uniform error mapping.
/// Implement <see cref="ExecuteAsync(TParams, EdgeContext, CancellationToken)"/>.
///
/// Accepted schemes can be declared two ways (attribute wins):
/// 1. Property: override <see cref="Schemes"/>
/// 2. Attribute: [EdgeScheme(EdgeAuthScheme.Hmac, EdgeAuthScheme.Session)]
/// </summary>
public abstract class EdgeHandlerBase<TParams> : IEdgeHandler
    where TParams : class, new()
{
    public abstract string Method { get; }

    /// <summary>
    /// Schemes this handler accepts — property form.
    /// Defaults to Hmac/Session; None is gateway-controlled.
    /// </summary>
    public virtual EdgeAuthScheme[] Schemes => [EdgeAuthScheme.Hmac, EdgeAuthScheme.Session];

    public async Task<EdgeRpcResponse> ExecuteAsync(EdgeRpcRequest request, EdgeContext context, CancellationToken ct)
    {
        TParams? parameters;
        try
        {
            parameters = request.Params.ValueKind == JsonValueKind.Undefined
                || request.Params.ValueKind == JsonValueKind.Null
                ? new TParams()
                : JsonSerializer.Deserialize<TParams>(request.Params, EdgeJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            return EdgeRpcResponse.Error(EdgeErrorCodes.ParamsInvalid, $"Params deserialization failed: {ex.Message}");
        }

        try
        {
            return await ExecuteAsync(parameters ?? new TParams(), context, ct).ConfigureAwait(false);
        }
        catch (EdgeException ex)
        {
            return EdgeRpcResponse.Error(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return EdgeRpcResponse.Error(EdgeErrorCodes.ParamsValidationFailed, ex.Message);
        }
        catch (Exception)
        {
            return EdgeError.InternalError("Internal error");
        }
    }

    /// <summary>Business logic implemented by subclasses.</summary>
    protected abstract Task<EdgeRpcResponse> ExecuteAsync(TParams parameters, EdgeContext context, CancellationToken ct);

    protected static EdgeRpcResponse Success(object? data = null) => EdgeRpcResponse.Success(data);
    protected static EdgeRpcResponse Error(string code, string message) => EdgeRpcResponse.Error(code, message);
}

/// <summary>
/// Edge business exception — thrown inside handlers, mapped to <see cref="EdgeRpcResponse"/> by the base class.
/// </summary>
public class EdgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
