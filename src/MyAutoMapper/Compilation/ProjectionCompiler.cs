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

            // TODO: holder-sharing not yet implemented — always creates fresh holder.
            // sharedHolderConstant and sharedHolderInfo are aspirational wiring for future
            // recursive calls to reuse the parent's closure holder instead of creating a new one.
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
                bool isExplicit = false;

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

                    isExplicit = true;
                    valueExpression = body;
                }
                else if (propertyMap.SourceExpression is LambdaExpression sourceLambda)
                {
                    isExplicit = true;
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
                    if (TryBuildNestedCollection(
                            valueExpression,
                            propertyMap.DestinationProperty.PropertyType,
                            catalog,
                            compilationStack,
                            sharedHolderConstant: holderConstant,
                            sharedHolderInfo: holderInfo,
                            out var nested))
                    {
                        valueExpression = nested!;
                    }
                    else if (CollectionProjectionBuilder.TryGetElementType(valueExpression.Type, out var srcElement)
                          && CollectionProjectionBuilder.TryGetElementType(propertyMap.DestinationProperty.PropertyType, out var dstElement)
                          && !propertyMap.DestinationProperty.PropertyType.IsAssignableFrom(valueExpression.Type))
                    {
                        if (isExplicit)
                        {
                            throw new InvalidOperationException(
                                $"Cannot map member '{propertyMap.DestinationProperty.DeclaringType?.Name}.{propertyMap.DestinationProperty.Name}': " +
                                $"no TypeMap registered to project elements '{srcElement.FullName}' -> '{dstElement.FullName}'. " +
                                $"Register the map via CreateMap<{srcElement.Name}, {dstElement.Name}>() or remove the explicit ForMember.");
                        }
                        // Both are collections with incompatible container types and no element TypeMap — skip binding.
                        // (If containers are compatible, fall through to Expression.Convert below.)
                        continue;
                    }
                    else
                    {
                        valueExpression = Expression.Convert(valueExpression, propertyMap.DestinationProperty.PropertyType);
                    }
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
                    if (TryBuildNestedCollection(
                            conventionExpr,
                            destProp.PropertyType,
                            catalog,
                            compilationStack,
                            sharedHolderConstant: holderConstant,
                            sharedHolderInfo: holderInfo,
                            out var nestedConv))
                    {
                        conventionExpr = nestedConv!;
                    }
                    else if (CollectionProjectionBuilder.TryGetElementType(conventionExpr.Type, out _)
                          && CollectionProjectionBuilder.TryGetElementType(destProp.PropertyType, out _)
                          && !destProp.PropertyType.IsAssignableFrom(conventionExpr.Type))
                    {
                        // Both are collections with incompatible container types and no element TypeMap — skip binding.
                        // (If containers are compatible, fall through to Expression.Convert below.)
                        continue;
                    }
                    else
                    {
                        conventionExpr = Expression.Convert(conventionExpr, destProp.PropertyType);
                    }
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

    private bool TryBuildNestedCollection(
        Expression sourceValue,
        Type destType,
        IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog,
        Stack<TypePair> stack,
        ConstantExpression? sharedHolderConstant,
        HolderTypeInfo? sharedHolderInfo,
        out Expression? result)
    {
        result = null;

        if (!CollectionProjectionBuilder.TryGetElementType(sourceValue.Type, out var srcElem))
            return false;
        if (!CollectionProjectionBuilder.TryGetElementType(destType, out var dstElem))
            return false;

        var elemPair = new TypePair(srcElem, dstElem);
        if (!catalog.TryGetValue(elemPair, out var elemConfig))
            return false;

        // Count how many times this pair already appears in the stack (recursion depth)
        var currentDepth = stack.Count(p => p == elemPair);
        var elemMaxDepth = elemConfig.MaxDepth ?? (currentDepth > 0 ? 3 : int.MaxValue);

        if (currentDepth >= elemMaxDepth)
        {
            // Exceeded depth — emit an empty collection
            result = BuildEmptyCollection(destType, dstElem);
            return true;
        }

        var inner = CompileProjection(
            elemPair,
            elemConfig.PropertyMaps,
            elemConfig.CustomConstructor,
            catalog,
            stack,
            sharedHolderConstant,
            sharedHolderInfo);

        var elemParam = Expression.Parameter(srcElem, "e");
        var innerLambda = inner.Projection;
        var innerBody = ParameterReplacer.Replace(innerLambda.Body, innerLambda.Parameters[0], elemParam);

        var elementLambda = Expression.Lambda(innerBody, elemParam);
        result = CollectionProjectionBuilder.BuildSelect(sourceValue, elementLambda, destType);
        return true;
    }

    private static Expression BuildEmptyCollection(Type destType, Type elementType)
    {
        if (destType.IsArray)
        {
            var arrayEmpty = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(elementType);
            return Expression.Call(arrayEmpty);
        }
        // List<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, etc.
        var listType = typeof(List<>).MakeGenericType(elementType);
        Expression newList = Expression.New(listType);
        if (destType != listType)
            newList = Expression.Convert(newList, destType);
        return newList;
    }
}
