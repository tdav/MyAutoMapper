using System.Linq.Expressions;
using MyAutoMapper.Parameters;

namespace MyAutoMapper.Configuration;

public interface IMemberOptions<TSource, TDest, TMember>
{
    void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

    void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression);

    void Ignore();
}
