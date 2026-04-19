using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Internal;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Extensions;

public static class QueryableExtensions
{
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Field initializer uses MakeGenericType only with primitive Type.MakeGenericMethodParameter " +
                        "to look up the Queryable.Select<,> method definition; the resulting MethodInfo is " +
                        "specialized lazily in GetSelectMethod, which is itself marked [RequiresDynamicCode].")]
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

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    private static MethodInfo GetSelectMethod(Type sourceType, Type destType)
        => SelectCache.GetOrAdd((sourceType, destType),
            key => SelectDefinition.MakeGenericMethod(key.Source, key.Dest));

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    [Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider) and inject IProjectionProvider via DI.",
              DiagnosticId = "SMAM0002")]
    public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source)
    {
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
        var projection = ProjectionProviderAccessor.Instance
            .GetProjection(source.ElementType, typeof(TDest));
#pragma warning restore SMAM0001
        return BuildQuery<TDest>(source, projection);
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    [Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider, Action<IParameterBinder>) and inject IProjectionProvider via DI.",
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

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    private static IQueryable<TDest> BuildQuery<TDest>(
        IQueryable source, LambdaExpression projection)
    {
        var select = GetSelectMethod(source.ElementType, typeof(TDest));
        var call = Expression.Call(select, source.Expression, Expression.Quote(projection));
        return source.Provider.CreateQuery<TDest>(call);
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    [Obsolete("Use ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider) and inject IProjectionProvider via DI.",
              DiagnosticId = "SMAM0002")]
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(this IQueryable<TSource> source)
    {
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
        var expression = ProjectionProviderAccessor.Instance.GetProjection<TSource, TDest>();
#pragma warning restore SMAM0001
        return source.Select(expression);
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    [Obsolete("Use ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider, Action<IParameterBinder>) and inject IProjectionProvider via DI.",
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

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source,
        IProjectionProvider provider)
    {
        var projection = provider.GetProjection(source.ElementType, typeof(TDest));
        return BuildQuery<TDest>(source, projection);
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public static IQueryable<TDest> ProjectTo<TDest>(
        this IQueryable source,
        IProjectionProvider provider,
        Action<IParameterBinder> parameters)
    {
        var binder = new ParameterBinder();
        parameters(binder);
        var projection = provider.GetProjection(source.ElementType, typeof(TDest), binder);
        return BuildQuery<TDest>(source, projection);
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(
        this IQueryable<TSource> source,
        IProjectionProvider provider)
    {
        var expression = provider.GetProjection<TSource, TDest>();
        return source.Select(expression);
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
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
