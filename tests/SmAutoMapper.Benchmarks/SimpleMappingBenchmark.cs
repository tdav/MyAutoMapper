using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SimpleMappingBenchmark
{
    private IMapper _myMapper = null!;
    private global::AutoMapper.IMapper _autoMapper = null!;
    private SimpleSource _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        // MyAutoMapper setup
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<MySimpleProfile>();
        var config = builder.Build();
        _myMapper = config.CreateMapper();

        // AutoMapper setup
        var amConfig = new global::AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SimpleSource, SimpleDest>();
        }, NullLoggerFactory.Instance);
        _autoMapper = amConfig.CreateMapper();

        // Mapster - uses static TypeAdapterConfig, auto-maps by convention
        TypeAdapterConfig.GlobalSettings.Compile();

        _source = new SimpleSource { Id = 1, Name = "Test Product", Price = 9.99m };
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Simple")]
    public SimpleDest Manual()
    {
        return new SimpleDest
        {
            Id = _source.Id,
            Name = _source.Name,
            Price = _source.Price
        };
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public SimpleDest MyAutoMapper()
    {
        return _myMapper.Map<SimpleSource, SimpleDest>(_source);
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public SimpleDest AutoMapper()
    {
        return _autoMapper.Map<SimpleDest>(_source);
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public SimpleDest Mapster()
    {
        return _source.Adapt<SimpleDest>();
    }
}

public class MySimpleProfile : MappingProfile
{
    public MySimpleProfile()
    {
        CreateMap<SimpleSource, SimpleDest>();
    }
}
