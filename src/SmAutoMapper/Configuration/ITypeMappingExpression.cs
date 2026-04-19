using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using SmAutoMapper.Internal;

namespace SmAutoMapper.Configuration;

public interface ITypeMappingExpression<TSource, TDest>
{
    ITypeMappingExpression<TSource, TDest> ForMember<TMember>(
        Expression<Func<TDest, TMember>> destinationMember,
        Action<IMemberOptions<TSource, TDest, TMember>> options);

    ITypeMappingExpression<TSource, TDest> Ignore(
        Expression<Func<TDest, object>> destinationMember);

    ITypeMappingExpression<TSource, TDest> ConstructUsing(
        Expression<Func<TSource, TDest>> constructor);

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    ITypeMappingExpression<TDest, TSource> ReverseMap();

    ITypeMappingExpression<TSource, TDest> MaxDepth(int depth);
}
