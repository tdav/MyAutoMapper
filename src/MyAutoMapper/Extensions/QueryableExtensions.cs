using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source)
    {
        var expression = ProjectionProviderAccessor.Instance.GetProjection<TSource, TDest>();
        return source.Select(expression);
    }

    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        Action<IParameterBinder> parameters)
    {
        var binder = new ParameterBinder();
        parameters(binder);
        var expression = ProjectionProviderAccessor.Instance.GetProjection<TSource, TDest>(binder);
        return source.Select(expression);
    }


    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        IProjectionProvider provider)
    {
        var expression = provider.GetProjection<TSource, TDest>();
        return source.Select(expression);
    }

    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        IProjectionProvider provider,
        Action<IParameterBinder> parameters)
    {
        var binder = new ParameterBinder();
        parameters(binder);
        var expression = provider.GetProjection<TSource, TDest>(binder);
        return source.Select(expression);
    }
}
