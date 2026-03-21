using System.Linq.Expressions;

namespace MyAutoMapper.Configuration;

public interface ITypeMappingExpression<TSource, TDest>
{
    ITypeMappingExpression<TSource, TDest> ForMember<TMember>(
        Expression<Func<TDest, TMember>> destinationMember,
        Action<IMemberOptions<TSource, TDest, TMember>> options);

    ITypeMappingExpression<TSource, TDest> Ignore(
        Expression<Func<TDest, object>> destinationMember);

    ITypeMappingExpression<TSource, TDest> ConstructUsing(
        Expression<Func<TSource, TDest>> constructor);

    ITypeMappingExpression<TDest, TSource> ReverseMap();
}
