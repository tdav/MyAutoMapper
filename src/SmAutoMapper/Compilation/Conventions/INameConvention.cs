using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Compilation.Conventions;

internal interface INameConvention
{
    bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression);
}
