namespace PubSoft.Contracts;

public enum EmitBehavior : byte
{
    Always = 0,
    IgnoreIfNull = 1,
    IgnoreIfDefault = 2
}

public enum FieldSource : byte
{
    Auto = 0,
    RouteParam = 1,
    Path = 2,
    Query = 3,
    Body = 4,
    Header = 5
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ApiFieldAttribute : Attribute
{
    public string Name { get; }
    public int Order { get; set; }
    public EmitBehavior Emit { get; set; } = EmitBehavior.Always;
    public EmitBehavior Behavior { get => Emit; set => Emit = value; }
    public FieldSource Source { get; set; } = FieldSource.Auto;

    /// <summary>Descriptive metadata only; this attribute does not encrypt values.</summary>
    public bool IsEncrypted { get; set; }

    public bool IsRequired { get; set; }
    public bool Flatten { get; set; }

    public ApiFieldAttribute(string name) => Name = name;
    public ApiFieldAttribute() => Name = string.Empty;
}