using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ComplexMappingBenchmark
{
    private IMapper _myMapper = null!;
    private global::AutoMapper.IMapper _autoMapper = null!;
    private ComplexSource _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        // SmAutoMapper setup
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<MyComplexProfile>();
        var config = builder.Build();
        _myMapper = config.CreateMapper();

        // AutoMapper setup
        var amConfig = new global::AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<ComplexSource, ComplexDest>();
        }, NullLoggerFactory.Instance);
        _autoMapper = amConfig.CreateMapper();

        // Mapster
        TypeAdapterConfig.GlobalSettings.Compile();

        _source = new ComplexSource
        {
            Id = 42,
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            Age = 30,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Salary = 75000m,
            Department = "Engineering",
            PhoneNumber = "+1-555-0123"
        };
    }

    [Benchmark(Baseline = true)]
    public ComplexDest Manual()
    {
        return new ComplexDest
        {
            Id = _source.Id,
            FirstName = _source.FirstName,
            LastName = _source.LastName,
            Email = _source.Email,
            Age = _source.Age,
            IsActive = _source.IsActive,
            CreatedAt = _source.CreatedAt,
            Salary = _source.Salary,
            Department = _source.Department,
            PhoneNumber = _source.PhoneNumber
        };
    }

    [Benchmark]
    public ComplexDest SmAutoMapper()
    {
        return _myMapper.Map<ComplexSource, ComplexDest>(_source);
    }

    [Benchmark]
    public ComplexDest AutoMapper()
    {
        return _autoMapper.Map<ComplexDest>(_source);
    }

    [Benchmark]
    public ComplexDest Mapster()
    {
        return _source.Adapt<ComplexDest>();
    }
}

public class MyComplexProfile : MappingProfile
{
    public MyComplexProfile()
    {
        CreateMap<ComplexSource, ComplexDest>();
    }
}
