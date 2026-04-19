using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Compilation.Conventions;

internal sealed class FlatteningConvention : INameConvention
{
    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression)
    {
        sourceExpression = TryFlatten(sourceType, destProperty.Name, destProperty.PropertyType, sourceParam);
        return sourceExpression is not null;
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    private static Expression? TryFlatten(
        Type currentType,
        string remainingName,
        Type destPropertyType,
        Expression currentExpression,
        int depth = 0)
    {
        if (string.IsNullOrEmpty(remainingName) || depth > 5)
            return null;

        var properties = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Try each property as a prefix
        foreach (var property in properties.OrderByDescending(p => p.Name.Length))
        {
            if (!remainingName.StartsWith(property.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var propertyAccess = Expression.Property(currentExpression, property);
            var remaining = remainingName[property.Name.Length..];

            if (remaining.Length == 0)
            {
                // Full match — check type compatibility
                if (destPropertyType.IsAssignableFrom(property.PropertyType))
                    return propertyAccess;
                continue;
            }

            // Try to continue flattening deeper
            if (!property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            {
                var deeper = TryFlatten(property.PropertyType, remaining, destPropertyType, propertyAccess, depth + 1);
                if (deeper is not null)
                {
                    var deeperExpr = deeper.Type != destPropertyType
                        ? Expression.Convert(deeper, destPropertyType)
                        : deeper;

                    return Expression.Condition(
                        Expression.Equal(propertyAccess, Expression.Constant(null, property.PropertyType)),
                        Expression.Default(destPropertyType),
                        deeperExpr);
                }
            }
        }

        return null;
    }
}
