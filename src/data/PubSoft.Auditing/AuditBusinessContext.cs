namespace PubSoft.Auditing;


/// <summary>Business-operation metadata attached to an audit entry.</summary>
/// <param name="Category">Operation category.</param>
/// <param name="Action">Operation name.</param>
/// <param name="TargetType">Target entity type.</param>
/// <param name="TargetId">Target identifier.</param>
/// <param name="TargetName">Human-readable target label.</param>
/// <param name="Detail">Free-form detail.</param>
public sealed record AuditBusinessContext(
    string? Category = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    string? TargetName = null,
    string? Detail = null);
