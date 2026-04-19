using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Compilation;

internal static class CollectionProjectionBuilder
{
    private static readonly MethodInfo EnumerableSelect =
        typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Select)
                        && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2);

    private static readonly MethodInfo EnumerableToList =
        typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!;

    private static readonly MethodInfo EnumerableToArray =
        typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!;

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public static Expression BuildSelect(Expression sourceCollection, LambdaExpression elementProjection, Type destType)
    {
        var srcElement = elementProjection.Parameters[0].Type;
        var dstElement = elementProjection.Body.Type;

        var select = EnumerableSelect.MakeGenericMethod(srcElement, dstElement);
        Expression call = Expression.Call(select, sourceCollection, elementProjection);

        if (destType.IsArray)
        {
            var toArray = EnumerableToArray.MakeGenericMethod(dstElement);
            return Expression.Call(toArray, call);
        }

        if (destType.IsGenericType)
        {
            var def = destType.GetGenericTypeDefinition();
            if (def == typeof(IEnumerable<>))
                return call;
        }

        // List<>, ICollection<>, IReadOnlyList<>, IList<> → ToList
        var toList = EnumerableToList.MakeGenericMethod(dstElement);
        return Expression.Call(toList, call);
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public static bool TryGetElementType(Type type, out Type elementType)
    {
        elementType = null!;
        if (type == typeof(string))
            return false;
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
                return false;
            elementType = type.GetElementType()!;
            return true;
        }
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>))
                return false;
            if (def == typeof(List<>) || def == typeof(IEnumerable<>) ||
                def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>) ||
                def == typeof(IReadOnlyCollection<>) || def == typeof(IList<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }
        if (type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
            return false;
        var ienum = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (ienum is not null)
        {
            elementType = ienum.GetGenericArguments()[0];
            return true;
        }
        return false;
    }
}
