using BenchmarkDotNet.Attributes;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Benchmarks;

[MemoryDiagnoser]
public class ProjectionBenchmark
{
    private IProjectionProvider _provider = null!;
    private IQueryable<ProjSource> _source = null!;

    public sealed class ProjSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class ProjDest
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Multiplier { get; set; }
    }

    public sealed class BenchProfile : MappingProfile
    {
        private readonly ParameterSlot<int> _mul;

        public BenchProfile()
        {
            _mul = DeclareParameter<int>("mul");

            CreateMap<ProjSource, ProjDest>()
                .ForMember(d => d.Multiplier, opt => opt.MapFrom<int>(_mul, (src, m) => m));
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<BenchProfile>();
        var config = builder.Build();
        _provider = config.CreateProjectionProvider();
        _source = Enumerable.Range(0, 1000)
            .Select(i => new ProjSource { Id = i, Name = $"n{i}" })
            .AsQueryable();
    }

    [Benchmark]
    public int ParameterizedProjection()
    {
        var result = _source
            .ProjectTo<ProjSource, ProjDest>(_provider, p => p.Set("mul", 3))
            .ToList();
        return result.Count;
    }
}

[MemoryDiagnoser]
public class ParameterizedProjectionBenchmark
{
    private IProjectionProvider provider = null!;
    private ParameterBinder binder = null!;

    public sealed class ScoreSource
    {
        public int Score { get; set; }
    }

    public sealed class ScoreDest
    {
        public int Score { get; set; }
        public bool Passes { get; set; }
    }

    public sealed class ScoreProfile : MappingProfile
    {
        private readonly ParameterSlot<int> minScore;

        public ScoreProfile()
        {
            minScore = DeclareParameter<int>("MinScore");

            CreateMap<ScoreSource, ScoreDest>()
                .ForMember(d => d.Passes, opt => opt.MapFrom<int>(minScore, (src, threshold) => src.Score >= threshold));
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ScoreProfile>();
        var config = builder.Build();
        this.provider = config.CreateProjectionProvider();
        this.binder = new ParameterBinder();
        this.binder.Set<int>("MinScore", 50);
    }

    [Benchmark]
    public object GetProjection()
    {
        return this.provider.GetProjection<ScoreSource, ScoreDest>(this.binder);
    }
}
