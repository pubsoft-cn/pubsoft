namespace PubSoft.Auditing;


/// <summary>One entity's property-level change captured during a request.</summary>
/// <param name="EntityType">Entity type name.</param>
/// <param name="EntityId">Primary key rendered as string.</param>
/// <param name="Kind">Change kind.</param>
/// <param name="ChangedAt">Capture time.</param>
/// <param name="Properties">Changed properties with old/new values.</param>
public sealed record AuditEntityChange(
    string EntityType,
    string EntityId,
    AuditChangeKind Kind,
    DateTimeOffset ChangedAt,
    IReadOnlyList<AuditPropertyChange> Properties);
