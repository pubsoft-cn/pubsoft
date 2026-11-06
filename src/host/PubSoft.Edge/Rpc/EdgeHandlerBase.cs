using System.Text.Json;

namespace PubSoft.Edge;


/// <summary>
/// Edge RPC handler base -- strongly-typed params deserialization + uniform error mapping.
/// Implement <see cref="ExecuteAsync(TParams, EdgeContext, CancellationToken)"/>.
///
/// Accepted schemes can be declared two ways (attribute wins):
/// 1. Property: override <see cref="Schemes"/>
/// 2. Attribute: [EdgeScheme(EdgeAuthScheme.Hmac, EdgeAuthScheme.Session)]
/// </summary>
public abstract class EdgeHandlerBase<TParams> : IEdgeHandler
    where TParams : class, new()
{
    /// <summary>The RPC method name this handler serves.</summary>
    public abstract string Method { get; }

    /// <summary>
    /// Schemes this handler accepts -- property form.
    /// Defaults to Hmac/Session; None is gateway-controlled.
    /// </summary>
    public virtual EdgeAuthScheme[] Schemes => [EdgeAuthScheme.Hmac, EdgeAuthScheme.Session];

    /// <summary>Runs the handler.</summary>
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
            return EdgeRpcResponse.ErrorWithSub(ex.Code, ex.Message, ex.SubCode, ex.SubMessage);
        }
        catch (ArgumentException ex)
        {
            return EdgeRpcResponse.Error(EdgeErrorCodes.ParamsValidationFailed, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // Unhandled downstream HTTP failure - honest 10503, not a generic 10500.
            // Raw transport detail goes to subMessage so the top-level message stays clean.
            return EdgeError.ServiceUnavailable("Upstream service unavailable") with { SubMessage = ex.Message };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout inside the handler (the request itself was not aborted).
            return EdgeError.GatewayTimeout("Upstream call timed out");
        }
        catch (Exception)
        {
            return EdgeError.InternalError("Internal error");
        }
    }

    /// <summary>Business logic implemented by subclasses.</summary>
    protected abstract Task<EdgeRpcResponse> ExecuteAsync(TParams parameters, EdgeContext context, CancellationToken ct);

    /// <summary>Success helper.</summary>
    protected static EdgeRpcResponse Success(object? data = null) => EdgeRpcResponse.Success(data);
    /// <summary>Error helper.</summary>
    protected static EdgeRpcResponse Error(string code, string message) => EdgeRpcResponse.Error(code, message);
}
