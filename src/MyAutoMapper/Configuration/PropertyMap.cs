using System.Linq.Expressions;
using System.Reflection;
using MyAutoMapper.Parameters;

namespace MyAutoMapper.Configuration;

public sealed class PropertyMap
{
    public PropertyInfo DestinationProperty { get; }
    public LambdaExpression? SourceExpression { get; internal set; }
    public bool IsIgnored { get; internal set; }
    public bool HasParameterizedSource { get; internal set; }
    public IParameterSlot? ParameterSlot { get; internal set; }
    public LambdaExpression? ParameterizedSourceExpression { get; internal set; }

    public PropertyMap(PropertyInfo destinationProperty)
    {
        DestinationProperty = destinationProperty;
    }
}
