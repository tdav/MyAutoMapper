namespace SmAutoMapper.Compilation;

internal static class CollectionProjectionBuilder
{
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
