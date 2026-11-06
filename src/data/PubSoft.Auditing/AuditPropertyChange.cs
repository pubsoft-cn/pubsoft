using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;


public sealed record AuditPropertyChange(string Name, JsonElement? OldValue, JsonElement? NewValue);
