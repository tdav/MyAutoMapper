using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Configuration;

internal sealed class TypeMapBuilder<TSource, TDest> : ITypeMappingExpression<TSource, TDest>, ITypeMapConfiguration
{
    private readonly List<PropertyMap> _propertyMaps = [];
    private readonly HashSet<string> _ignoredMembers = [];
    private readonly List<string> _skippedReverseProperties = [];
    private LambdaExpression? _customConstructor;
    private TypeMapBuilder<TDest, TSource>? _reverseMap;

    public Type SourceType => typeof(TSource);
    public Type DestinationType => typeof(TDest);
    public IReadOnlyList<PropertyMap> PropertyMaps => _propertyMaps;
    public LambdaExpression? CustomConstructor => _customConstructor;
    public ITypeMapConfiguration? ReverseTypeMap => _reverseMap;
    public IReadOnlyList<string> SkippedReverseProperties => _skippedReverseProperties;
    private int? _maxDepth = null;
    public int? MaxDepth => _maxDepth;

    public ITypeMappingExpression<TSource, TDest> ForMember<TMember>(
        Expression<Func<TDest, TMember>> destinationMember,
        Action<IMemberOptions<TSource, TDest, TMember>> options)
    {
        var propertyInfo = ExtractPropertyInfo(destinationMember);
        var builder = new MemberMapBuilder<TSource, TDest, TMember>();
        options(builder);
        var propertyMap = new PropertyMap(
            propertyInfo,
            SourceExpression: builder.SourceExpression,
            IsIgnored: builder.IsIgnored,
            HasParameterizedSource: builder.HasParameterizedSource,
            ParameterSlot: builder.ParameterSlot,
            ParameterizedSourceExpression: builder.ParameterizedSourceExpression);
        _propertyMaps.Add(propertyMap);
        return this;
    }

    public ITypeMappingExpression<TSource, TDest> Ignore(
        Expression<Func<TDest, object>> destinationMember)
    {
        var propertyInfo = ExtractPropertyInfo(destinationMember);
        _ignoredMembers.Add(propertyInfo.Name);
        var propertyMap = new PropertyMap(propertyInfo, IsIgnored: true);
        _propertyMaps.Add(propertyMap);
        return this;
    }

    public ITypeMappingExpression<TSource, TDest> ConstructUsing(
        Expression<Func<TSource, TDest>> constructor)
    {
        _customConstructor = constructor;
        return this;
    }

    public ITypeMappingExpression<TDest, TSource> ReverseMap()
    {
        _reverseMap = new TypeMapBuilder<TDest, TSource>();

        // Auto-configure reverse for simple property-to-property mappings
        foreach (var propertyMap in _propertyMaps)
        {
            var propName = propertyMap.DestinationProperty.Name;

            if (propertyMap.IsIgnored)
            {
                _skippedReverseProperties.Add($"{propName} (ignored)");
                continue;
            }

            if (propertyMap.HasParameterizedSource)
            {
                _skippedReverseProperties.Add($"{propName} (parameterized)");
                continue;
            }

            if (propertyMap.SourceExpression is LambdaExpression sourceLambda
                && sourceLambda.Body is MemberExpression memberExpr
                && memberExpr.Member is PropertyInfo sourceProperty)
            {
                if (!sourceProperty.CanWrite)
                {
                    _skippedReverseProperties.Add($"{propName} (readonly)");
                    continue;
                }

                // Simple property mapping: d.X = s.Y => reverse: d.Y = s.X
                var destPropertyOnReverse = sourceProperty; // becomes destination in reverse
                var sourcePropertyOnReverse = propertyMap.DestinationProperty; // becomes source in reverse

                if (destPropertyOnReverse.CanWrite)
                {
                    var reverseSourceParam = Expression.Parameter(typeof(TDest), "src");
                    var reverseSourceExpr = Expression.Lambda(
                        Expression.Property(reverseSourceParam, sourcePropertyOnReverse),
                        reverseSourceParam);
                    var reversePropertyMap = new PropertyMap(destPropertyOnReverse, SourceExpression: reverseSourceExpr);
                    _reverseMap._propertyMaps.Add(reversePropertyMap);
                }
            }
            else if (propertyMap.SourceExpression is not null)
            {
                _skippedReverseProperties.Add($"{propName} (computed)");
            }
        }

        return _reverseMap;
    }

    /// <summary>
    /// Extracts PropertyInfo from a member selector lambda expression.
    /// Handles UnaryExpression (Convert) wrapping for value types boxed to object.
    /// Handles nested member access like d => d.Address.City.
    /// </summary>
    private static PropertyInfo ExtractPropertyInfo<T>(Expression<Func<TDest, T>> expression)
    {
        var body = expression.Body;

        // Unwrap UnaryExpression (Convert) for value types
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression memberExpression && memberExpression.Member is PropertyInfo propertyInfo)
        {
            return propertyInfo;
        }

        throw new ArgumentException(
            $"Expression '{expression}' does not refer to a property. " +
            "Use a simple property access expression like 'd => d.PropertyName'.");
    }
}
