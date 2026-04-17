using System.Linq.Expressions;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

public interface IMemberOptions<TSource, TDest, TMember>
{
    void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

    void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression);

    /// <summary>
    /// Maps from a source expression whose return type differs from <typeparamref name="TMember"/>.
    /// Primary use case: collections where the element type differs
    /// (e.g. <c>List&lt;Category&gt;</c> -> <c>List&lt;CategoryViewModel&gt;</c>).
    /// Requires a registered <c>TypeMap</c> for the element pair; otherwise the configuration compiler throws.
    /// </summary>
    void MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> sourceExpression);

    /// <summary>
    /// Parameterised variant of <see cref="MapFrom{TSourceMember}(Expression{Func{TSource, TSourceMember}})"/>.
    /// </summary>
    void MapFrom<TSourceMember, TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TSourceMember>> sourceExpression);

    void Ignore();
}
