using System.Linq.Expressions;

namespace MyAutoMapper.Compilation;

internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParameter;
    private readonly Expression _replacement;

    private ParameterReplacer(ParameterExpression oldParameter, Expression replacement)
    {
        _oldParameter = oldParameter;
        _replacement = replacement;
    }

    protected override Expression VisitParameter(ParameterExpression node)
        => node == _oldParameter ? _replacement : base.VisitParameter(node);

    public static Expression Replace(Expression expression, ParameterExpression oldParameter, Expression replacement)
        => new ParameterReplacer(oldParameter, replacement).Visit(expression);
}
