using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Compilation.Conventions;

internal interface INameConvention
{
    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression);
}
