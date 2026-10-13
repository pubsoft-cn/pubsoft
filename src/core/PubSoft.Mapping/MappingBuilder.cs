using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace PubSoft.Mapping;

/// <summary>Builds a mapping using explicit expressions, constants, ignored properties, and name matching.</summary>
public sealed class MappingBuilder<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TSource,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TDestination>
    where TDestination : new()
{
    private readonly Dictionary<string, LambdaExpression> _explicit = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _constants = new(StringComparer.Ordinal);

    /// <summary>Maps a destination property from an expression over the source.</summary>
    public MappingBuilder<TSource, TDestination> Map<TSourceProperty, TDestinationProperty>(
        Expression<Func<TDestination, TDestinationProperty>> destinationProperty,
        Expression<Func<TSource, TSourceProperty>> sourceExpression)
    {
        _explicit[GetPropertyName(destinationProperty)] = sourceExpression;
        return this;
    }

    /// <summary>Sets a destination property to a constant value.</summary>
    public MappingBuilder<TSource, TDestination> Const<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationProperty,
        TProperty value)
    {
        _constants[GetPropertyName(destinationProperty)] = value;
        return this;
    }

    /// <summary>Leaves a destination property at its default value.</summary>
    public MappingBuilder<TSource, TDestination> Ignore<TProperty>(
        Expression<Func<TDestination, TProperty>> destinationProperty)
    {
        _ignored.Add(GetPropertyName(destinationProperty));
        return this;
    }

    internal Func<TSource, TDestination> Compile()
    {
        var sourceParameter = Expression.Parameter(typeof(TSource), "source");
        var sourceProperties = typeof(TSource)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true }
                && property.GetIndexParameters().Length == 0)
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var destinationProperties = typeof(TDestination)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true }
                && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        ValidateConfiguredProperties(destinationProperties);

        var bindings = new List<MemberBinding>();
        foreach (var destinationProperty in destinationProperties.Values)
        {
            if (_ignored.Contains(destinationProperty.Name))
                continue;

            if (_constants.TryGetValue(destinationProperty.Name, out var constant))
            {
                bindings.Add(Expression.Bind(destinationProperty,
                    Expression.Constant(constant, destinationProperty.PropertyType)));
                continue;
            }

            if (_explicit.TryGetValue(destinationProperty.Name, out var sourceExpression))
            {
                var body = new ParameterReplacer(sourceExpression.Parameters[0], sourceParameter)
                    .Visit(sourceExpression.Body)!;
                bindings.Add(Expression.Bind(destinationProperty,
                    ObjectMapper.ConvertType(body, destinationProperty.PropertyType)));
                continue;
            }

            var sourceProperty = ObjectMapper.FindSourceProperty(sourceProperties, destinationProperty.Name);
            if (sourceProperty is null)
                continue;

            var sourceAccess = Expression.Property(sourceParameter, sourceProperty);
            bindings.Add(Expression.Bind(destinationProperty,
                ObjectMapper.ConvertType(sourceAccess, destinationProperty.PropertyType)));
        }

        var initializer = Expression.MemberInit(Expression.New(typeof(TDestination)), bindings);
        return Expression.Lambda<Func<TSource, TDestination>>(initializer, sourceParameter).Compile();
    }

    private void ValidateConfiguredProperties(IReadOnlyDictionary<string, PropertyInfo> destinationProperties)
    {
        foreach (var name in _explicit.Keys.Concat(_constants.Keys).Concat(_ignored))
        {
            if (!destinationProperties.ContainsKey(name))
                throw new ArgumentException($"Destination property '{name}' is not publicly writable.");
        }
    }

    private static string GetPropertyName<TProperty>(Expression<Func<TDestination, TProperty>> selector)
    {
        Expression body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            body = conversion.Operand;

        if (body is MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression parameter }
            && parameter == selector.Parameters[0])
            return property.Name;

        throw new ArgumentException("Expression must select a direct destination property.", nameof(selector));
    }

    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression destination)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == source ? destination : base.VisitParameter(node);
    }
}