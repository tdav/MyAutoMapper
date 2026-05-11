using System.Linq.Expressions;

namespace SmAutoMapper.Compilation;

internal sealed class ClosureValueInjector : ExpressionVisitor
{
    private readonly Type _holderType;
    private readonly object _newHolderInstance;

    private ClosureValueInjector(Type holderType, object newHolderInstance)
    {
        _holderType = holderType;
        _newHolderInstance = newHolderInstance;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        // Replace ConstantExpression nodes whose type matches the closure holder type
        if (node.Type == _holderType)
        {
            return Expression.Constant(_newHolderInstance, _holderType);
        }
        return base.VisitConstant(node);
    }

    /// <summary>
    /// Swaps all closure holder instances in the expression tree with a new holder containing updated parameter values.
    /// The tree shape remains identical, enabling EF Core query plan reuse.
    /// </summary>
    public static Expression<Func<TSource, TDest>> InjectParameters<TSource, TDest>(
        Expression<Func<TSource, TDest>> template,
        Type holderType,
        object newHolderInstance)
    {
        var injector = new ClosureValueInjector(holderType, newHolderInstance);
        return (Expression<Func<TSource, TDest>>)injector.Visit(template);
    }

    /// <summary>
    /// Non-generic version for when types are not known at compile time.
    /// </summary>
    public static LambdaExpression InjectParameters(
        LambdaExpression template,
        Type holderType,
        object newHolderInstance)
    {
        var injector = new ClosureValueInjector(holderType, newHolderInstance);
        return (LambdaExpression)injector.Visit(template);
    }
}
