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
            elementType = type.GetElementType()!;
            return true;
        }
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>))
                return false;
            if (def == typeof(List<>) || def == typeof(IEnumerable<>) ||
                def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>) ||
                def == typeof(IReadOnlyCollection<>) || def == typeof(IList<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }
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
