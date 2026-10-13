namespace PubSoft.Contracts;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ApiOperationAttribute(string operationId) : Attribute
{
    public string OperationId { get; } = operationId;
    public string[] Aliases { get; init; } = [];
    public ApiHttpVerb Verb { get; init; } = ApiHttpVerb.Post;
}