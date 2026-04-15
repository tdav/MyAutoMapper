using FluentAssertions;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.UnitTests.Compilation;

public class MapperConfigurationTests
{
    private class SimpleProfile : MappingProfile
    {
        public SimpleProfile()
        {
            CreateMap<SimpleSource, SimpleDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.Price, o => o.MapFrom(s => s.Price));
        }
    }

    [Fact]
    public void Build_CreatesMapperConfiguration()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();
        config.Should().NotBeNull();
    }

    [Fact]
    public void GetTypeMap_ReturnsCorrectTypeMap()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();

        var typeMap = config.GetTypeMap<SimpleSource, SimpleDest>();
        typeMap.Should().NotBeNull();
        typeMap.TypePair.SourceType.Should().Be(typeof(SimpleSource));
        typeMap.TypePair.DestinationType.Should().Be(typeof(SimpleDest));
    }

    [Fact]
    public void GetTypeMap_UnmappedPair_ThrowsInvalidOperation()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();

        var act = () => config.GetTypeMap<SimpleDest, SimpleSource>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_CompilesProjectionExpression()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();

        var typeMap = config.GetTypeMap<SimpleSource, SimpleDest>();
        typeMap.ProjectionExpression.Should().NotBeNull();
    }

    [Fact]
    public void Build_CompilesDelegate()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();

        var typeMap = config.GetTypeMap<SimpleSource, SimpleDest>();
        typeMap.CompiledDelegate.Should().NotBeNull();
    }

    private class ReverseProfile : MappingProfile
    {
        public ReverseProfile()
        {
            CreateMap<SimpleSource, SimpleDest>().ReverseMap();
        }
    }

    private class ExplicitReverseProfile : MappingProfile
    {
        public ExplicitReverseProfile()
        {
            CreateMap<SimpleDest, SimpleSource>();
        }
    }

    [Fact]
    public void Build_DuplicateTypePairAcrossProfiles_LastWins_DoesNotThrow()
    {
        // Profile 1 registers (SimpleSource, SimpleDest) + reverse (SimpleDest, SimpleSource).
        // Profile 2 registers (SimpleDest, SimpleSource) again — duplicate pair across profiles.
        // Previously (pre-two-phase) the indexer assignment silently overwrote; the two-phase
        // catalog must preserve that last-wins semantic rather than throwing on duplicates.
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ReverseProfile>();
        builder.AddProfile<ExplicitReverseProfile>();

        var act = () => builder.Build();
        act.Should().NotThrow();
    }
}
