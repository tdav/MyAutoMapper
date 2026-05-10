using System.Linq.Expressions;

namespace SmAutoMapper.Compilation;

internal sealed class ClosureValueInjector : ExpressionVisitor
{
    [ThreadStatic]
    private static ClosureValueInjector? instance;

    private Type? holderType;
    private object? newHolderInstance;

    private ClosureValueInjector() { }

    private void Initialize(Type holderType, object newHolderInstance)
    {
        this.holderType = holderType;
        this.newHolderInstance = newHolderInstance;
    }

    private void Reset()
    {
        this.holderType = null;
        this.newHolderInstance = null;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == this.holderType)
            return Expression.Constant(this.newHolderInstance, this.holderType);
        return base.VisitConstant(node);
    }

    /// <summary>
    /// Swaps all closure holder instances in the expression tree with a new holder containing
    /// updated parameter values. Reuses a per-thread injector to avoid heap allocation per call.
    /// The tree shape remains identical, enabling EF Core query plan reuse.
    /// </summary>
    public static Expression<Func<TSource, TDest>> InjectParameters<TSource, TDest>(
        Expression<Func<TSource, TDest>> template,
        Type holderType,
        object newHolderInstance)
    {
        var injector = instance ??= new ClosureValueInjector();
        try
        {
            injector.Initialize(holderType, newHolderInstance);
            return (Expression<Func<TSource, TDest>>)injector.Visit(template);
        }
        finally
        {
            injector.Reset();
        }
    }

    /// <summary>
    /// Non-generic version for when types are not known at compile time.
    /// </summary>
    public static LambdaExpression InjectParameters(
        LambdaExpression template,
        Type holderType,
        object newHolderInstance)
    {
        var injector = instance ??= new ClosureValueInjector();
        try
        {
            injector.Initialize(holderType, newHolderInstance);
            return (LambdaExpression)injector.Visit(template);
        }
        finally
        {
            injector.Reset();
        }
    }
}
