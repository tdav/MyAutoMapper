using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Logging;
using MyAutoMapper.Compilation;
using MyAutoMapper.Configuration;

namespace MyAutoMapper.Validation;

internal sealed class ConfigurationValidator
{
    private readonly ILogger<ConfigurationValidator>? _logger;

    public ConfigurationValidator(ILogger<ConfigurationValidator>? logger = null)
    {
        _logger = logger;
    }

    public void Validate(IReadOnlyCollection<TypeMap> typeMaps)
    {
        var errors = new List<string>();

        foreach (var typeMap in typeMaps)
        {
            ValidateTypeMap(typeMap, errors);
        }

        if (errors.Count > 0)
        {
            throw new MappingValidationException(errors);
        }
    }

    private void ValidateTypeMap(TypeMap typeMap, List<string> errors)
    {
        var sourceType = typeMap.TypePair.SourceType;
        var destType = typeMap.TypePair.DestinationType;
        var mappingName = $"{sourceType.Name} -> {destType.Name}";

        // Get mapped and ignored property names
        var mappedProperties = new HashSet<string>(
            typeMap.PropertyMaps.Select(pm => pm.DestinationProperty.Name));

        // Validate explicitly configured properties have valid source types
        foreach (var propertyMap in typeMap.PropertyMaps)
        {
            if (propertyMap.IsIgnored)
                continue;

            if (propertyMap.SourceExpression is LambdaExpression sourceLambda)
            {
                var sourceReturnType = sourceLambda.ReturnType;
                var destPropertyType = propertyMap.DestinationProperty.PropertyType;

                if (!IsAssignableOrConvertible(sourceReturnType, destPropertyType))
                {
                    errors.Add(
                        $"[{mappingName}] Property '{propertyMap.DestinationProperty.Name}': " +
                        $"source type '{sourceReturnType.Name}' is not assignable to destination type '{destPropertyType.Name}'.");
                }
            }
        }

        // Check for unmapped writable destination properties
        // that are NOT resolvable via conventions (same-name or flattening)
        var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        foreach (var destProp in destProperties)
        {
            if (mappedProperties.Contains(destProp.Name))
                continue;

            // Check if convention can resolve this property (same-name match)
            var sourceProperty = sourceType.GetProperty(
                destProp.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (sourceProperty is not null && IsAssignableOrConvertible(sourceProperty.PropertyType, destProp.PropertyType))
                continue;

            // The ProjectionCompiler's auto-convention will handle flattening,
            // so we don't warn about properties that can be resolved by convention.
            // Only warn about truly unmappable properties as informational (not error).
            // This matches AutoMapper behavior — unmapped properties get default values.
            _logger?.LogWarning(
                "Mapping {Mapping}: destination property '{Property}' is not mapped and will receive default value",
                mappingName, destProp.Name);
        }
    }

    private static bool IsAssignableOrConvertible(Type source, Type destination)
    {
        if (destination.IsAssignableFrom(source))
            return true;

        // Allow numeric conversions
        if (IsNumericType(source) && IsNumericType(destination))
            return true;

        // Allow nullable unwrapping/wrapping
        var underlyingSource = Nullable.GetUnderlyingType(source) ?? source;
        var underlyingDest = Nullable.GetUnderlyingType(destination) ?? destination;
        if (underlyingDest.IsAssignableFrom(underlyingSource))
            return true;

        return false;
    }

    private static bool IsNumericType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(byte) || t == typeof(sbyte) ||
               t == typeof(short) || t == typeof(ushort) ||
               t == typeof(int) || t == typeof(uint) ||
               t == typeof(long) || t == typeof(ulong) ||
               t == typeof(float) || t == typeof(double) ||
               t == typeof(decimal);
    }
}
