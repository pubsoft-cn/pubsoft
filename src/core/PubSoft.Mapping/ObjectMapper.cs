using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace PubSoft.Mapping;

/// <summary>Maps objects using cached expression-compiled delegates.</summary>
public static class ObjectMapper
{
    private static readonly ConcurrentDictionary<TypePair, Delegate> s_mappings = new();
    private static readonly ConcurrentDictionary<TypePair, Delegate> s_converters = new();

    /// <summary>Registers a type converter. Register converters before the first map for a type pair.</summary>
    public static void Convert<TSource, TDestination>(Func<TSource, TDestination> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        s_converters[new TypePair(typeof(TSource), typeof(TDestination))] = converter;
    }

    /// <summary>Registers and compiles a mapping configuration.</summary>
    public static void Register<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TSource,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TDestination>(
        Action<MappingBuilder<TSource, TDestination>>? configure = null)
        where TDestination : new()
    {
        var builder = new MappingBuilder<TSource, TDestination>();
        configure?.Invoke(builder);
        s_mappings[new TypePair(typeof(TSource), typeof(TDestination))] = builder.Compile();
    }

    /// <summary>Maps one object, compiling and caching a property-name mapping when needed.</summary>
    public static TDestination Map<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TSource,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TDestination>(
        TSource source)
        where TDestination : new()
    {
        if (source is null)
            return default!;

        var key = new TypePair(typeof(TSource), typeof(TDestination));
        var mapping = s_mappings.GetOrAdd(key, _ => new MappingBuilder<TSource, TDestination>().Compile());
        return ((Func<TSource, TDestination>)mapping)(source);
    }

    /// <summary>Maps a sequence into a list, preserving null elements for reference destinations.</summary>
    public static List<TDestination> MapList<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TSource,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TDestination>(
        IEnumerable<TSource> source)
        where TDestination : new()
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = source.TryGetNonEnumeratedCount(out var count)
            ? new List<TDestination>(count)
            : [];
        foreach (var item in source)
            result.Add(Map<TSource, TDestination>(item));
        return result;
    }

    internal static PropertyInfo? FindSourceProperty(
        IReadOnlyDictionary<string, PropertyInfo> sourceProperties,
        string destinationName)
    {
        if (sourceProperties.TryGetValue(destinationName, out var property))
            return property;

        const string suffix = "Ms";
        if (destinationName.EndsWith(suffix, StringComparison.Ordinal)
            && sourceProperties.TryGetValue(destinationName[..^suffix.Length], out property))
            return property;

        return sourceProperties.TryGetValue(destinationName + suffix, out property) ? property : null;
    }

    internal static Expression ConvertType(Expression source, Type destinationType)
    {
        var sourceType = source.Type;
        if (destinationType == sourceType || destinationType.IsAssignableFrom(sourceType))
            return source;

        if (s_converters.TryGetValue(new TypePair(sourceType, destinationType), out var converter))
            return Expression.Invoke(Expression.Constant(converter), source);

        var sourceNullable = Nullable.GetUnderlyingType(sourceType);
        if (sourceNullable is not null)
        {
            var destinationNullable = Nullable.GetUnderlyingType(destinationType);
            var nonNullableDestination = destinationNullable ?? destinationType;
            // Convert throws InvalidOperationException on null, matching Nullable<T>.Value.
            var value = Expression.Convert(source, sourceNullable);
            var convertedValue = ConvertType(value, nonNullableDestination);
            var presentValue = destinationNullable is null
                ? convertedValue
                : Expression.Convert(convertedValue, destinationType);
            return Expression.Condition(
                Expression.NotEqual(source, Expression.Constant(null, sourceType)),
                presentValue,
                Expression.Default(destinationType));
        }

        var targetNullable = Nullable.GetUnderlyingType(destinationType);
        if (targetNullable is not null)
            return Expression.Convert(ConvertType(source, targetNullable), destinationType);

        if (sourceType == typeof(int) && destinationType == typeof(bool))
            return Expression.Equal(source, Expression.Constant(0));

        if (sourceType == typeof(long) && destinationType == typeof(string))
        {
            var toString = typeof(long).GetMethod(nameof(long.ToString), [typeof(IFormatProvider)])!;
            return Expression.Call(source, toString, Expression.Constant(CultureInfo.InvariantCulture));
        }

        if ((sourceType.IsEnum || destinationType.IsEnum || IsNumeric(sourceType) && IsNumeric(destinationType))
            && CanConvert(sourceType, destinationType))
            return Expression.Convert(source, destinationType);

        throw new InvalidOperationException(
            $"Cannot map property type '{sourceType}' to '{destinationType}'. Register a type converter or explicit mapping.");
    }

    private static bool IsNumeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
            or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            or TypeCode.Decimal or TypeCode.Double or TypeCode.Single;
    }

    private static bool CanConvert(Type sourceType, Type destinationType)
    {
        try
        {
            _ = Expression.Convert(Expression.Parameter(sourceType), destinationType);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private readonly record struct TypePair(Type Source, Type Destination);
}