using System.Linq.Expressions;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

internal sealed class MemberMapBuilder<TSource, TDest, TMember> : IMemberOptions<TSource, TDest, TMember>
{
    internal LambdaExpression? SourceExpression { get; private set; }
    internal bool IsIgnored { get; private set; }
    internal bool HasParameterizedSource { get; private set; }
    internal IParameterSlot? ParameterSlot { get; private set; }
    internal LambdaExpression? ParameterizedSourceExpression { get; private set; }

    public void MapFrom(Expression<Func<TSource, TMember>> sourceExpression)
        => SourceExpression = sourceExpression;

    public void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression)
    {
        HasParameterizedSource = true;
        ParameterSlot = parameter;
        ParameterizedSourceExpression = sourceExpression;
    }

    public void MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> sourceExpression)
        => SourceExpression = sourceExpression;

    public void MapFrom<TSourceMember, TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TSourceMember>> sourceExpression)
    {
        HasParameterizedSource = true;
        ParameterSlot = parameter;
        ParameterizedSourceExpression = sourceExpression;
    }

    public void Ignore() => IsIgnored = true;
}
