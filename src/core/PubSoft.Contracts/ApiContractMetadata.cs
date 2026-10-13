namespace PubSoft.Contracts;

public sealed class ApiContractMetadata
{
    public string OperationId { get; init; } = string.Empty;
    public string RoutePrefix { get; init; } = string.Empty;
    public string RouteAction { get; init; } = string.Empty;
    public string RouteSuffix { get; init; } = string.Empty;
    public ApiHttpVerb Verb { get; init; } = ApiHttpVerb.Post;
    public Type RequestType { get; init; } = null!;
    public Type ResponseType { get; init; } = null!;
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public INamingPolicy NamingPolicy { get; init; } = CamelCaseNamingPolicy.Instance;
}