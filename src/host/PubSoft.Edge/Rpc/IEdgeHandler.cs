using System.Text.Json;

namespace PubSoft.Edge;

/// <summary>
/// Edge RPC handler -- one method maps to one handler.
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
