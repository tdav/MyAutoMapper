using System.Linq.Expressions;
using MyAutoMapper.Parameters;

namespace MyAutoMapper.Configuration;

internal sealed class MemberMapBuilder<TSource, TDest, TMember> : IMemberOptions<TSource, TDest, TMember>
{
    private readonly PropertyMap _propertyMap;

    public MemberMapBuilder(PropertyMap propertyMap)
    {
        _propertyMap = propertyMap;
    }

    public void MapFrom(Expression<Func<TSource, TMember>> sourceExpression)
    {
        _propertyMap.SourceExpression = sourceExpression;
    }

    public void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression)
    {
        _propertyMap.HasParameterizedSource = true;
        _propertyMap.ParameterSlot = parameter;
        _propertyMap.ParameterizedSourceExpression = sourceExpression;
    }

    public void Ignore()
    {
        _propertyMap.IsIgnored = true;
    }
}
