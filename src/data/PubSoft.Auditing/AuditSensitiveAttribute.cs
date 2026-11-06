using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Auditing;

/// <summary>
/// Declarative sensitive-field marker — put on an entity property to have its values
/// redacted in audit capture, without listing the name in <see cref="AuditChangeCollectorOptions.SensitiveProperties"/>.
/// Both paths converge: attribute wins over name matching.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AuditSensitiveAttribute : Attribute { }
