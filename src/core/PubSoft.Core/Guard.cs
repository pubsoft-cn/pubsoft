using System.Runtime.CompilerServices;

namespace PubSoft.Core;

public static class Guard
{
    public static string NotNull(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value is null) throw new DomainException($"{name} must not be null");
        return value;
    }

    public static string NotEmpty(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{name} must not be empty");
        return value!;
    }

    public static string MaxLength(string value, int maxLength, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        NotNull(value, name);
        if (value.Length > maxLength) throw new DomainException($"{name} exceeds max length {maxLength}: {value.Length}");
        return value;
    }

    public static string Phone(string value)
    {
        NotNull(value, nameof(Phone));
        MaxLength(value, 16, nameof(Phone));
        if (!value.All(char.IsDigit)) throw new DomainException("Phone must be digits only");
        return value;
    }

    public static T EnumDefined<T>(T value, [CallerArgumentExpression(nameof(value))] string? name = null) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new DomainException($"{name} invalid enum: {value}");
        return value;
    }

    public static T FlagsDefined<T>(T value, [CallerArgumentExpression(nameof(value))] string? name = null) where T : struct, Enum
    {
        var mask = Enum.GetValues<T>().Select(ToUInt64Bits).Aggregate(0UL, (current, flag) => current | flag);
        if ((ToUInt64Bits(value) & ~mask) != 0) throw new DomainException($"{name} has undefined flag bits: {value}");
        return value;
    }

    private static ulong ToUInt64Bits<T>(T value) where T : struct, Enum
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))) switch
        {
            TypeCode.SByte => unchecked((ulong)Convert.ToSByte(value, System.Globalization.CultureInfo.InvariantCulture)),
            TypeCode.Byte => Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture),
            TypeCode.Int16 => unchecked((ulong)Convert.ToInt16(value, System.Globalization.CultureInfo.InvariantCulture)),
            TypeCode.UInt16 => Convert.ToUInt16(value, System.Globalization.CultureInfo.InvariantCulture),
            TypeCode.Int32 => unchecked((ulong)Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)),
            TypeCode.UInt32 => Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture),
            TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
            TypeCode.UInt64 => Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Unsupported enum underlying type.")
        };
    }
}

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// HTTP status the exception middleware surfaces for this domain violation.
    /// Default 400; use 409 for conflicts (e.g. "already registered"). Values
    /// outside the 4xx range are clamped to 400 — domain errors never map to 5xx.
    /// </summary>
    public int StatusCode { get; init; } = 400;
}
