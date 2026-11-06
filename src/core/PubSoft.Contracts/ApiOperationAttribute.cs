namespace PubSoft.Contracts;

/// <summary>Declares a contract class as an outbound API operation.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ApiOperationAttribute(string operationId) : Attribute
{
    /// <summary>The operation identifier.</summary>
    public string OperationId { get; } = operationId;
    /// <summary>Alternate operation ids accepted for the same operation.</summary>
    public string[] Aliases { get; init; } = [];
    /// <summary>HTTP verb for the operation.</summary>
    public ApiHttpVerb Verb { get; init; } = ApiHttpVerb.Post;
}