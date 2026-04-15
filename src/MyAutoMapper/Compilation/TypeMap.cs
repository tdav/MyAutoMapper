using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Configuration;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Compilation;

public sealed class TypeMap
{
    public TypePair TypePair { get; }
    public IReadOnlyList<PropertyMap> PropertyMaps { get; }
    public LambdaExpression? ProjectionExpression { get; }
    public Delegate? CompiledDelegate { get; }
    public LambdaExpression? CustomConstructor { get; }
    public IReadOnlyList<IParameterSlot> DeclaredParameters { get; }

    // Stores the closure holder type info for parameterized projections (immutable after construction)
    public Type? ClosureHolderType { get; }
    public object? DefaultClosureHolder { get; }
    internal IReadOnlyDictionary<string, PropertyInfo>? HolderPropertyMap { get; }

    public int? MaxDepth { get; }

    internal TypeMap(
        TypePair typePair,
        IReadOnlyList<PropertyMap> propertyMaps,
        LambdaExpression? customConstructor,
        IReadOnlyList<IParameterSlot> declaredParameters,
        LambdaExpression? projectionExpression,
        Delegate? compiledDelegate,
        Type? closureHolderType,
        object? defaultClosureHolder,
        IReadOnlyDictionary<string, PropertyInfo>? holderPropertyMap,
        int? maxDepth)
    {
        TypePair = typePair;
        PropertyMaps = propertyMaps;
        CustomConstructor = customConstructor;
        DeclaredParameters = declaredParameters;
        ProjectionExpression = projectionExpression;
        CompiledDelegate = compiledDelegate;
        ClosureHolderType = closureHolderType;
        DefaultClosureHolder = defaultClosureHolder;
        HolderPropertyMap = holderPropertyMap;
        MaxDepth = maxDepth;
    }
}

/// <summary>
/// Result of expression tree compilation, returned by ProjectionCompiler.
/// Separates compilation output from the TypeMap to avoid side-effects.
/// </summary>
internal sealed record CompilationResult(
    LambdaExpression Projection,
    Type? ClosureHolderType,
    object? DefaultClosureHolder,
    IReadOnlyDictionary<string, System.Reflection.PropertyInfo>? HolderPropertyMap);
