using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using SmAutoMapper.Internal;

namespace SmAutoMapper.Compilation;

internal sealed class InMemoryCompiler
{
    /// <summary>
    /// Takes a projection expression and compiles it into a Func delegate
    /// with a null-check wrapper: if source is null, return default.
    /// </summary>
    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public Delegate CompileDelegate(TypePair typePair, LambdaExpression projectionExpr)
    {
        var sourceParam = projectionExpr.Parameters[0];
        var sourceType = typePair.SourceType;
        var destType = typePair.DestinationType;

        // Wrap with null check: source == null ? default(TDest) : <projection>
        Expression body;
        if (!sourceType.IsValueType)
        {
            body = Expression.Condition(
                Expression.Equal(sourceParam, Expression.Constant(null, sourceType)),
                Expression.Default(destType),
                projectionExpr.Body);
        }
        else
        {
            body = projectionExpr.Body;
        }

        var delegateType = typeof(Func<,>).MakeGenericType(sourceType, destType);
        var wrappedLambda = Expression.Lambda(delegateType, body, sourceParam);
        return wrappedLambda.Compile();
    }
}
