namespace PubSoft.Contracts;

/// <summary>Resolved metadata describing one outbound API operation.</summary>
public sealed class ApiContractMetadata
{
    /// <summary>The operation identifier.</summary>
    public string OperationId { get; init; } = string.Empty;
    /// <summary>Route prefix prepended to the action route.</summary>
    public string RoutePrefix { get; init; } = string.Empty;
    /// <summary>The action route.</summary>
    public string RouteAction { get; init; } = string.Empty;
    /// <summary>Route suffix appended after the action route.</summary>
    public string RouteSuffix { get; init; } = string.Empty;
    /// <summary>HTTP verb for the operation.</summary>
    public ApiHttpVerb Verb { get; init; } = ApiHttpVerb.Post;
    /// <summary>CLR type of the request contract.</summary>
    public Type RequestType { get; init; } = null!; // pubsoft:exempt PUBSOFT046 "populated via metadata generators; required cannot be satisfied there"
    /// <summary>CLR type of the response contract.</summary>
    public Type ResponseType { get; init; } = null!; // pubsoft:exempt PUBSOFT046 "populated via metadata generators; required cannot be satisfied there"
    /// <summary>Alternate operation ids accepted for the same operation.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
    /// <summary>Field-name policy for the wire format.</summary>
    public INamingPolicy NamingPolicy { get; init; } = CamelCaseNamingPolicy.Instance;
}