using System.Collections.Concurrent;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Compilation;

public sealed class MapperConfiguration
{
    private readonly ConcurrentDictionary<TypePair, TypeMap> _typeMaps = new();
    private readonly List<ITypeMapConfiguration> _typeMapConfigs = [];
    private readonly ProjectionCompiler _projectionCompiler = new();
    private readonly InMemoryCompiler _inMemoryCompiler = new();

    internal MapperConfiguration(IReadOnlyList<MappingProfile> profiles)
    {
        // Phase 1: collect all ITypeMapConfiguration and build catalog
        var allConfigs = new List<ITypeMapConfiguration>();
        foreach (var profile in profiles)
        {
            foreach (var cfg in profile.TypeMaps)
            {
                allConfigs.Add(cfg);
                if (cfg.ReverseTypeMap is not null)
                    allConfigs.Add(cfg.ReverseTypeMap);
            }
        }

        // Use indexer assignment (last-wins) instead of ToDictionary to preserve
        // pre-existing silent override semantics of `_typeMaps[typePair] = typeMap`
        // when the same TypePair is registered across multiple profiles (e.g.
        // CreateMap<A,B>().ReverseMap() in one profile + CreateMap<B,A>() in another).
        var catalogDict = new Dictionary<TypePair, ITypeMapConfiguration>();
        foreach (var c in allConfigs)
            catalogDict[new TypePair(c.SourceType, c.DestinationType)] = c;
        IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog = catalogDict;

        // Phase 2: compilation
        foreach (var cfg in allConfigs)
        {
            _typeMapConfigs.Add(cfg);
            BuildAndRegisterTypeMap(cfg, catalog);
        }
    }

    public TypeMap GetTypeMap<TSource, TDest>()
        => GetTypeMap(typeof(TSource), typeof(TDest));

    public TypeMap GetTypeMap(Type sourceType, Type destType)
    {
        var typePair = new TypePair(sourceType, destType);
        if (_typeMaps.TryGetValue(typePair, out var typeMap))
            return typeMap;

        throw new InvalidOperationException(
            $"No mapping configured for {sourceType.Name} -> {destType.Name}. " +
            "Ensure a MappingProfile with CreateMap<TSource, TDest>() has been registered.");
    }

    internal bool HasTypeMap(Type sourceType, Type destType)
        => _typeMaps.ContainsKey(new TypePair(sourceType, destType));

    public IMapper CreateMapper() => new Mapper(this);

    public IProjectionProvider CreateProjectionProvider() => new ProjectionProvider(this);

    internal IReadOnlyCollection<TypeMap> GetAllTypeMaps() => _typeMaps.Values.ToList();

    internal IReadOnlyCollection<ITypeMapConfiguration> GetAllTypeMapConfigurations() => _typeMapConfigs;

    private void BuildAndRegisterTypeMap(
        ITypeMapConfiguration typeMapConfig,
        IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog)
    {
        var typePair = new TypePair(typeMapConfig.SourceType, typeMapConfig.DestinationType);

        // Collect parameter slots used in this type map's property maps
        var usedParams = typeMapConfig.PropertyMaps
            .Where(pm => pm.HasParameterizedSource && pm.ParameterSlot is not null)
            .Select(pm => pm.ParameterSlot!)
            .DistinctBy(s => s.Name)
            .ToList();

        // Compile projection expression (returns immutable result, no side effects)
        var compilationResult = _projectionCompiler.CompileProjection(
            typePair,
            typeMapConfig.PropertyMaps,
            typeMapConfig.CustomConstructor,
            catalog,
            compilationStack: null);

        // Compile in-memory delegate from the projection expression
        var compiledDelegate = _inMemoryCompiler.CompileDelegate(
            typePair, compilationResult.Projection);

        // Build fully immutable TypeMap
        var typeMap = new TypeMap(
            typePair,
            typeMapConfig.PropertyMaps,
            typeMapConfig.CustomConstructor,
            usedParams,
            compilationResult.Projection,
            compiledDelegate,
            compilationResult.ClosureHolderType,
            compilationResult.DefaultClosureHolder,
            compilationResult.HolderPropertyMap,
            typeMapConfig.MaxDepth);

        _typeMaps[typePair] = typeMap;
    }
}
