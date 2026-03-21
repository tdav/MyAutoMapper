using MyAutoMapper.Compilation;

namespace MyAutoMapper.Runtime;

public sealed class Mapper : IMapper
{
    private readonly MapperConfiguration _configuration;

    public Mapper(MapperConfiguration configuration)
    {
        _configuration = configuration;
    }

    public TDest Map<TSource, TDest>(TSource source)
    {
        var typeMap = _configuration.GetTypeMap<TSource, TDest>();

        if (typeMap.CompiledDelegate is null)
            throw new InvalidOperationException(
                $"No compiled delegate for {typeof(TSource).Name} -> {typeof(TDest).Name}.");

        var func = (Func<TSource, TDest>)typeMap.CompiledDelegate;
        return func(source);
    }
}
