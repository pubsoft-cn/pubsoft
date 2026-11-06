using System.Text.Json;

namespace PubSoft.Edge;


/// <summary>
/// Edge business exception -- thrown inside handlers, mapped to <see cref="EdgeRpcResponse"/> by the base class.
/// <paramref name="subCode"/> carries the optional machine-readable business detail
/// (convention: <c>{domain}.{detail}</c>, e.g. <c>params.plate_invalid</c>);
/// <paramref name="subMessage"/> carries the optional human-readable detail for it.
/// </summary>
public class EdgeException(string code, string message, string? subCode = null, string? subMessage = null) : Exception(message)
{
    /// <summary>Edge error code.</summary>
    public string Code { get; } = code;

    /// <summary>Business sub-code surfaced as <see cref="EdgeRpcResponse.SubCode"/>.</summary>
    public string? SubCode { get; } = subCode;

    /// <summary>Business detail surfaced as <see cref="EdgeRpcResponse.SubMessage"/>.</summary>
    public string? SubMessage { get; } = subMessage;
}
