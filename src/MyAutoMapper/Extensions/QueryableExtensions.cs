using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Extensions;

public static class QueryableExtensions
{
    private static readonly MethodInfo SelectDefinition =
        typeof(Queryable).GetMethods()
            .First(m => m.Name == "Select"
                && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.GetGenericArguments()[0]
                    .GetGenericArguments().Length == 2);

    private static readonly ConcurrentDictionary<(Type Source, Type Dest), MethodInfo> SelectCache = new();

    private static MethodInfo GetSelectMethod(Type sourceType, Type destType)
        => SelectCache.GetOrAdd((sourceType, destType),
            key => SelectDefinition.MakeGenericMethod(key.Source, key.Dest));


    public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source)
    {
        var projection = ProjectionProviderAccessor.Instance
            .GetProjection(source.ElementType, typeof(TDest));
        var select = GetSelectMethod(source.ElementType, typeof(TDest));
        var call = Expression.Call(select, source.Expression, Expression.Quote(projection));
        return source.Provider.CreateQuery<TDest>(call);
    }

    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source,
        Action<IParameterBinder> parameters)
    {
        var binder = new ParameterBinder();
        parameters(binder);
        var projection = ProjectionProviderAccessor.Instance
            .GetProjection(source.ElementType, typeof(TDest), binder);
        var select = GetSelectMethod(source.ElementType, typeof(TDest));
        var call = Expression.Call(select, source.Expression, Expression.Quote(projection));
        return source.Provider.CreateQuery<TDest>(call);
    }

    public static IQueryable<TDest> ProjectTo<TSource, TDest>(this IQueryable<TSource> source)
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
