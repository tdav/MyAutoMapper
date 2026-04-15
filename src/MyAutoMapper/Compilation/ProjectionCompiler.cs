using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Compilation.Conventions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Compilation;

internal sealed class ProjectionCompiler
{
    private readonly ClosureHolderFactory _holderFactory = new();
    private readonly INameConvention[] _conventions =
    [
        new DefaultNameConvention(),
        new FlatteningConvention()
    ];

    /// <summary>
    /// Builds an Expression&lt;Func&lt;TSource, TDest&gt;&gt; from property mappings and configuration.
    /// Returns a CompilationResult without mutating any input.
    /// </summary>
    public CompilationResult CompileProjection(
        TypePair typePair,
        IReadOnlyList<PropertyMap> propertyMaps,
        LambdaExpression? customConstructor,
        IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog,
        Stack<TypePair>? compilationStack = null,
        int? maxDepth = null,
        ConstantExpression? sharedHolderConstant = null,
        HolderTypeInfo? sharedHolderInfo = null)
    {
        compilationStack ??= new Stack<TypePair>();
        compilationStack.Push(typePair);
        try
        {
            var sourceType = typePair.SourceType;
            var destType = typePair.DestinationType;
            var sourceParam = Expression.Parameter(sourceType, "src");

            var bindings = new List<MemberBinding>();

            // Collect explicitly mapped destination property names
            var explicitlyMapped = new HashSet<string>(
                propertyMaps.Select(pm => pm.DestinationProperty.Name));

            // Check if this mapping has parameterized properties
            var parameterizedMaps = propertyMaps
                .Where(pm => pm.HasParameterizedSource && pm.ParameterSlot is not null)
                .ToList();

            HolderTypeInfo? holderInfo = null;
            object? defaultHolder = null;
            ConstantExpression? holderConstant = null;

            if (parameterizedMaps.Count > 0)
            {
                var slots = parameterizedMaps
                    .Select(pm => pm.ParameterSlot!)
                    .DistinctBy(s => s.Name)
                    .ToList();

                holderInfo = _holderFactory.GetOrCreateHolderType(slots);
                defaultHolder = holderInfo.CreateDefaultInstance();
                holderConstant = Expression.Constant(defaultHolder, holderInfo.HolderType);
            }

            // Process explicitly configured property maps
            foreach (var propertyMap in propertyMaps)
            {
                if (propertyMap.IsIgnored)
                    continue;

                Expression valueExpression;

                if (propertyMap.HasParameterizedSource
                    && propertyMap.ParameterizedSourceExpression is LambdaExpression paramLambda
                    && propertyMap.ParameterSlot is not null
                    && holderInfo is not null
                    && holderConstant is not null)
                {
                    var srcLambdaParam = paramLambda.Parameters[0];
                    var paramLambdaParam = paramLambda.Parameters[1];

                    var holderPropertyAccess = Expression.Property(
                        holderConstant,
                        holderInfo.PropertyMap[propertyMap.ParameterSlot.Name]);

                    var body = ParameterReplacer.Replace(paramLambda.Body, srcLambdaParam, sourceParam);
                    body = ParameterReplacer.Replace(body, paramLambdaParam, holderPropertyAccess);

                    valueExpression = body;
                }
                else if (propertyMap.SourceExpression is LambdaExpression sourceLambda)
                {
                    valueExpression = ParameterReplacer.Replace(
                        sourceLambda.Body,
                        sourceLambda.Parameters[0],
                        sourceParam);
                }
                else
                {
                    // Explicit ForMember with no MapFrom — try convention
                    Expression? conventionExpr = null;
                    foreach (var convention in _conventions)
                    {
                        if (convention.TryGetSourceExpression(sourceType, propertyMap.DestinationProperty, sourceParam, out conventionExpr))
                            break;
                    }
                    if (conventionExpr is null)
                        continue;
                    valueExpression = conventionExpr;
                }

                if (valueExpression.Type != propertyMap.DestinationProperty.PropertyType)
                {
                    valueExpression = Expression.Convert(valueExpression, propertyMap.DestinationProperty.PropertyType);
                }

                bindings.Add(Expression.Bind(propertyMap.DestinationProperty, valueExpression));
            }

            // Auto-convention: map destination properties not explicitly configured
            var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && !explicitlyMapped.Contains(p.Name));

            foreach (var destProp in destProperties)
            {
                Expression? conventionExpr = null;
                foreach (var convention in _conventions)
                {
                    if (convention.TryGetSourceExpression(sourceType, destProp, sourceParam, out conventionExpr))
                        break;
                }
                if (conventionExpr is null)
                    continue;

                if (conventionExpr.Type != destProp.PropertyType)
                {
                    conventionExpr = Expression.Convert(conventionExpr, destProp.PropertyType);
                }

                bindings.Add(Expression.Bind(destProp, conventionExpr));
            }

            // Build the final MemberInit expression
            Expression initExpression;
            if (customConstructor is LambdaExpression ctorLambda)
            {
                var ctorBody = ParameterReplacer.Replace(
                    ctorLambda.Body,
                    ctorLambda.Parameters[0],
                    sourceParam);

                if (bindings.Count > 0 && ctorBody is NewExpression newExpr)
                {
                    initExpression = Expression.MemberInit(newExpr, bindings);
                }
                else
                {
                    initExpression = ctorBody;
                }
            }
            else
            {
                var newExpression = Expression.New(destType);
                initExpression = Expression.MemberInit(newExpression, bindings);
            }

            var delegateType = typeof(Func<,>).MakeGenericType(sourceType, destType);
            var projection = Expression.Lambda(delegateType, initExpression, sourceParam);

            return new CompilationResult(
                projection,
                holderInfo?.HolderType,
                defaultHolder,
                holderInfo?.PropertyMap);
        }
        finally
        {
            compilationStack.Pop();
        }
    }
}
