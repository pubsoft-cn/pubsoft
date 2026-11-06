namespace PubSoft.Contracts;

/// <summary>Whether a field is emitted on the wire.</summary>
public enum EmitBehavior : byte
{
    /// <summary>Always emit, even when null/default.</summary>
    Always = 0,
    /// <summary>Omit when the value is null.</summary>
    IgnoreIfNull = 1,
    /// <summary>Omit when the value is the type default.</summary>
    IgnoreIfDefault = 2
}

/// <summary>Where a field binds on the wire.</summary>
public enum FieldSource : byte
{
    /// <summary>Infer from verb and contract shape.</summary>
    Auto = 0,
    /// <summary>Binds into a route placeholder.</summary>
    RouteParam = 1,
    /// <summary>Binds into the path segment.</summary>
    Path = 2,
    /// <summary>Binds as a query parameter.</summary>
    Query = 3,
    /// <summary>Binds into the request body.</summary>
    Body = 4,
    /// <summary>Binds as a request header.</summary>
    Header = 5
}

/// <summary>Overrides how one property maps onto the wire for an outbound contract.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ApiFieldAttribute : Attribute
{

    /// <summary>Binds the property under an explicit wire name.</summary>
    public ApiFieldAttribute(string name) => Name = name;
    /// <summary>Binds the property under its CLR name.</summary>
    public ApiFieldAttribute() => Name = string.Empty;
    /// <summary>The wire name of the field.</summary>
    public string Name { get; }
    /// <summary>Serialization order hint.</summary>
    public int Order { get; set; }
    /// <summary>Whether the field is emitted.</summary>
    public EmitBehavior Emit { get; set; } = EmitBehavior.Always;
    /// <summary>Alias of <see cref="Emit"/> kept for compatibility.</summary>
    public EmitBehavior Behavior { get => Emit; set => Emit = value; }
    /// <summary>Where the field binds on the wire.</summary>
    public FieldSource Source { get; set; } = FieldSource.Auto;

    /// <summary>Descriptive metadata only; this attribute does not encrypt values.</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>Whether a missing value fails validation.</summary>
    public bool IsRequired { get; set; }
    /// <summary>Whether the nested object's fields are flattened into the parent.</summary>
    public bool Flatten { get; set; }
}