using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FlatteningBenchmark
{
    private IMapper _myMapper = null!;
    private global::AutoMapper.IMapper _autoMapper = null!;
    private FlattenSource _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        // MyAutoMapper setup — flattening convention handles AddressStreet, AddressCity, etc.
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<MyFlatteningProfile>();
        var config = builder.Build();
        _myMapper = config.CreateMapper();

        // AutoMapper setup — flattening is built-in by convention
        var amConfig = new global::AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<FlattenSource, FlattenDest>();
        }, NullLoggerFactory.Instance);
        _autoMapper = amConfig.CreateMapper();

        // Mapster — configure flattening via Unflattening is not needed;
        // Mapster supports flattening out of the box with matching naming
        TypeAdapterConfig<FlattenSource, FlattenDest>.NewConfig()
            .Map(d => d.AddressStreet, s => s.Address.Street)
            .Map(d => d.AddressCity, s => s.Address.City)
            .Map(d => d.AddressZipCode, s => s.Address.ZipCode)
            .Map(d => d.AddressCountry, s => s.Address.Country)
            .Compile();

        _source = new FlattenSource
        {
            Id = 1,
            Title = "Sample Order",
            Address = new FlattenAddress
            {
                Street = "123 Main St",
                City = "Springfield",
                ZipCode = "62704",
                Country = "USA"
            }
        };
    }

    [Benchmark(Baseline = true)]
    public FlattenDest Manual()
    {
        return new FlattenDest
        {
            Id = _source.Id,
            Title = _source.Title,
            AddressStreet = _source.Address.Street,
            AddressCity = _source.Address.City,
            AddressZipCode = _source.Address.ZipCode,
            AddressCountry = _source.Address.Country
        };
    }

    [Benchmark]
    public FlattenDest MyAutoMapper()
    {
        return _myMapper.Map<FlattenSource, FlattenDest>(_source);
    }

    [Benchmark]
    public FlattenDest AutoMapper()
    {
        return _autoMapper.Map<FlattenDest>(_source);
    }

    [Benchmark]
    public FlattenDest Mapster()
    {
        return _source.Adapt<FlattenDest>();
    }
}

public class MyFlatteningProfile : MappingProfile
{
    public MyFlatteningProfile()
    {
        CreateMap<FlattenSource, FlattenDest>();
    }
}
