using System.Linq.Expressions;

namespace MyAutoMapper.Configuration;

public interface ITypeMapConfiguration
{
    Type SourceType { get; }
    Type DestinationType { get; }
    IReadOnlyList<PropertyMap> PropertyMaps { get; }
    LambdaExpression? CustomConstructor { get; }
    ITypeMapConfiguration? ReverseTypeMap { get; }
    IReadOnlyList<string> SkippedReverseProperties { get; }
}
