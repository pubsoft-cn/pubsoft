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
            TypeCode.SByte => unchecked((ulong)Convert.ToSByte(value)),
            TypeCode.Byte => Convert.ToByte(value),
            TypeCode.Int16 => unchecked((ulong)Convert.ToInt16(value)),
            TypeCode.UInt16 => Convert.ToUInt16(value),
            TypeCode.Int32 => unchecked((ulong)Convert.ToInt32(value)),
            TypeCode.UInt32 => Convert.ToUInt32(value),
            TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value)),
            TypeCode.UInt64 => Convert.ToUInt64(value),
            _ => throw new InvalidOperationException("Unsupported enum underlying type.")
        };
    }
}

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
