using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Internal;

namespace SmAutoMapper.Compilation.Conventions;

internal interface INameConvention
{
    // Attributed on the interface so AOT analyzer flags callers through virtual dispatch;
    // every implementer must keep matching attributes in sync.
    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression);
}
