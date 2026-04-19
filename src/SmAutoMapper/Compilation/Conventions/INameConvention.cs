using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Internal;

namespace SmAutoMapper.Compilation.Conventions;

internal interface INameConvention
{
    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression);
}
