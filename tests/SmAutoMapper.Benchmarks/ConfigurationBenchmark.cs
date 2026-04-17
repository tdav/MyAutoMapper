using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using SmAutoMapper.Configuration;

namespace SmAutoMapper.Benchmarks;

/// <summary>
/// Measures the one-time cost of configuring each mapper library
/// (profile registration, expression compilation, etc.).
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ConfigurationBenchmark
{
    [Benchmark(Baseline = true)]
    public object MyAutoMapper_Configure()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ConfigBenchmarkProfile>();
        return builder.Build();
    }

    [Benchmark]
    public object AutoMapper_Configure()
    {
        var config = new global::AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SimpleSource, SimpleDest>();
            cfg.CreateMap<ComplexSource, ComplexDest>();
            cfg.CreateMap<FlattenSource, FlattenDest>();
        }, NullLoggerFactory.Instance);
        // Force compilation of all maps
        config.CompileMappings();
        return config;
    }

    [Benchmark]
    public object Mapster_Configure()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<SimpleSource, SimpleDest>();
        config.NewConfig<ComplexSource, ComplexDest>();
        config.NewConfig<FlattenSource, FlattenDest>()
            .Map(d => d.AddressStreet, s => s.Address.Street)
            .Map(d => d.AddressCity, s => s.Address.City)
            .Map(d => d.AddressZipCode, s => s.Address.ZipCode)
            .Map(d => d.AddressCountry, s => s.Address.Country);
        config.Compile();
        return config;
    }
}

public class ConfigBenchmarkProfile : MappingProfile
{
    public ConfigBenchmarkProfile()
    {
        CreateMap<SimpleSource, SimpleDest>();
        CreateMap<ComplexSource, ComplexDest>();
        CreateMap<FlattenSource, FlattenDest>();
    }
}
