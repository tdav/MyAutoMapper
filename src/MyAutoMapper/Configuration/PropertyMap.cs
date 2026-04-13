using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

public sealed record PropertyMap(
    PropertyInfo DestinationProperty,
    LambdaExpression? SourceExpression = null,
    bool IsIgnored = false,
    bool HasParameterizedSource = false,
    IParameterSlot? ParameterSlot = null,
    LambdaExpression? ParameterizedSourceExpression = null);
