using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


/// <summary>One property's old/new value inside an entity change.</summary>
/// <param name="Name">Property name.</param>
/// <param name="OldValue">Value before the change.</param>
/// <param name="NewValue">Value after the change.</param>
public sealed record AuditPropertyChange(string Name, JsonElement? OldValue, JsonElement? NewValue);
