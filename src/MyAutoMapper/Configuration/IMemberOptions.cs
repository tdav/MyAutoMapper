using System.Linq.Expressions;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

public interface IMemberOptions<TSource, TDest, TMember>
{
    void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

    void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression);

    void Ignore();
}
