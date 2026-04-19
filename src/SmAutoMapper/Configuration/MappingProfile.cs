using System.Diagnostics.CodeAnalysis;
using SmAutoMapper.Internal;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

public abstract class MappingProfile
{
    internal List<ITypeMapConfiguration> TypeMaps { get; } = [];

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var builder = new TypeMapBuilder<TSource, TDest>();
        TypeMaps.Add(builder);
        return builder;
    }

    protected ParameterSlot<T> DeclareParameter<T>(string name)
        => new(name);
}
