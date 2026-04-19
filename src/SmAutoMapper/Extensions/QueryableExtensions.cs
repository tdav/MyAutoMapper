using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Extensions;

public static class QueryableExtensions
{
    private static readonly MethodInfo SelectDefinition =
        typeof(Queryable).GetMethod(
            nameof(Queryable.Select),
            genericParameterCount: 2,
            types: new[]
            {
                typeof(IQueryable<>).MakeGenericType(Type.MakeGenericMethodParameter(0)),
                typeof(Expression<>).MakeGenericType(
                    typeof(Func<,>).MakeGenericType(
                        Type.MakeGenericMethodParameter(0),
                        Type.MakeGenericMethodParameter(1)))
            })!;

    private static readonly ConcurrentDictionary<(Type Source, Type Dest), MethodInfo> SelectCache = new();

    private static MethodInfo GetSelectMethod(Type sourceType, Type destType)
        => SelectCache.GetOrAdd((sourceType, destType),
            key => SelectDefinition.MakeGenericMethod(key.Source, key.Dest));

    [Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider) and inject IProjectionProvider via DI. " +
              "Will be removed in 2.0.",
              DiagnosticId = "SMAM0002")]
    public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source)
    {
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
        var projection = ProjectionProviderAccessor.Instance
            .GetProjection(source.ElementType, typeof(TDest));
#pragma warning restore SMAM0001
        return BuildQuery<TDest>(source, projection);
    }

    [Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider, Action<IParameterBinder>) and inject IProjectionProvider via DI. " +
              "Will be removed in 2.0.",
              DiagnosticId = "SMAM0002")]
    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source,
        Action<IParameterBinder> parameters)
    {
        var binder = new ParameterBinder();
        parameters(binder);
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
        var projection = ProjectionProviderAccessor.Instance
            .GetProjection(source.ElementType, typeof(TDest), binder);
#pragma warning restore SMAM0001
        return BuildQuery<TDest>(source, projection);
    }

    private static IQueryable<TDest> BuildQuery<TDest>(
        IQueryable source, LambdaExpression projection)
    {
        var select = GetSelectMethod(source.ElementType, typeof(TDest));
        var call = Expression.Call(select, source.Expression, Expression.Quote(projection));
        return source.Provider.CreateQuery<TDest>(call);
    }

    [Obsolete("Use ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider) and inject IProjectionProvider via DI. " +
              "Will be removed in 2.0.",
              DiagnosticId = "SMAM0002")]
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(this IQueryable<TSource> source)
    {
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
        var expression = ProjectionProviderAccessor.Instance.GetProjection<TSource, TDest>();
#pragma warning restore SMAM0001
        return source.Select(expression);
    }

    [Obsolete("Use ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider, Action<IParameterBinder>) and inject IProjectionProvider via DI. " +
              "Will be removed in 2.0.",
              DiagnosticId = "SMAM0002")]
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        Action<IParameterBinder> parameters)
    {
        var binder = new ParameterBinder();
        parameters(binder);
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
        var expression = ProjectionProviderAccessor.Instance.GetProjection<TSource, TDest>(binder);
#pragma warning restore SMAM0001
        return source.Select(expression);
    }

    /// <summary>
    /// Projects the query's elements to <typeparamref name="TDest"/> using the supplied
    /// <paramref name="provider"/>. Recommended migration target for the legacy accessor-based overload.
    /// </summary>
    /// <typeparam name="TDest">Destination type to project to.</typeparam>
    /// <param name="source">Source queryable whose element type is inferred at runtime.</param>
    /// <param name="provider">Projection provider obtained from DI (registered by <c>AddMapping</c>).</param>
    /// <returns>Queryable of <typeparamref name="TDest"/> that composes with EF Core.</returns>
    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source,
        IProjectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(provider);

        var projection = provider.GetProjection(source.ElementType, typeof(TDest));
        return BuildQuery<TDest>(source, projection);
    }

    /// <summary>
    /// Projects the query's elements to <typeparamref name="TDest"/> using the supplied
    /// <paramref name="provider"/> and binds runtime parameters via <paramref name="parameters"/>.
    /// </summary>
    /// <typeparam name="TDest">Destination type to project to.</typeparam>
    /// <param name="source">Source queryable whose element type is inferred at runtime.</param>
    /// <param name="provider">Projection provider obtained from DI (registered by <c>AddMapping</c>).</param>
    /// <param name="parameters">Callback that binds named parameters used by the projection.</param>
    /// <returns>Queryable of <typeparamref name="TDest"/> with parameters applied.</returns>
    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source,
        IProjectionProvider provider,
        Action<IParameterBinder> parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(parameters);

        var binder = new ParameterBinder();
        parameters(binder);
        var projection = provider.GetProjection(source.ElementType, typeof(TDest), binder);
        return BuildQuery<TDest>(source, projection);
    }

    /// <summary>
    /// Projects an <see cref="IQueryable{TSource}"/> to <typeparamref name="TDest"/> using the supplied
    /// <paramref name="provider"/>. Preferred strongly-typed migration target.
    /// </summary>
    /// <typeparam name="TSource">Source element type.</typeparam>
    /// <typeparam name="TDest">Destination type to project to.</typeparam>
    /// <param name="source">Source queryable.</param>
    /// <param name="provider">Projection provider obtained from DI (registered by <c>AddMapping</c>).</param>
    /// <returns>Queryable of <typeparamref name="TDest"/> that composes with EF Core.</returns>
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        IProjectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(provider);

        var expression = provider.GetProjection<TSource, TDest>();
        return source.Select(expression);
    }

    /// <summary>
    /// Projects an <see cref="IQueryable{TSource}"/> to <typeparamref name="TDest"/> using the supplied
    /// <paramref name="provider"/> and binds runtime parameters via <paramref name="parameters"/>.
    /// </summary>
    /// <typeparam name="TSource">Source element type.</typeparam>
    /// <typeparam name="TDest">Destination type to project to.</typeparam>
    /// <param name="source">Source queryable.</param>
    /// <param name="provider">Projection provider obtained from DI (registered by <c>AddMapping</c>).</param>
    /// <param name="parameters">Callback that binds named parameters used by the projection.</param>
    /// <returns>Queryable of <typeparamref name="TDest"/> with parameters applied.</returns>
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        IProjectionProvider provider,
        Action<IParameterBinder> parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(parameters);

        var binder = new ParameterBinder();
        parameters(binder);
        var expression = provider.GetProjection<TSource, TDest>(binder);
        return source.Select(expression);
    }
}
