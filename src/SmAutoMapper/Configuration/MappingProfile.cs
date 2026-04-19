using System.Diagnostics.CodeAnalysis;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

public abstract class MappingProfile
{
    internal List<ITypeMapConfiguration> TypeMaps { get; } = [];

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var builder = new TypeMapBuilder<TSource, TDest>();
        TypeMaps.Add(builder);
        return builder;
    }

    protected ParameterSlot<T> DeclareParameter<T>(string name)
        => new(name);
}
