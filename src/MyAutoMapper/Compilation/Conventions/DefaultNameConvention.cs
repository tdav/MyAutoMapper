using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Compilation.Conventions;

internal sealed class DefaultNameConvention : INameConvention
{
    public bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression)
    {
        var sourceProperty = sourceType.GetProperty(
            destProperty.Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (sourceProperty is not null && IsAssignable(sourceProperty.PropertyType, destProperty.PropertyType))
        {
            sourceExpression = Expression.Property(sourceParam, sourceProperty);
            return true;
        }

        sourceExpression = null;
        return false;
    }

    private static bool IsAssignable(Type sourceType, Type destType)
    {
        if (destType.IsAssignableFrom(sourceType))
            return true;

        // Handle nullable wrapping: int -> int?
        var underlyingDest = Nullable.GetUnderlyingType(destType);
        if (underlyingDest is not null && underlyingDest.IsAssignableFrom(sourceType))
            return true;

        return false;
    }
}
