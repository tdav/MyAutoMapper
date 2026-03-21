using FluentAssertions;
using MyAutoMapper.Configuration;
using MyAutoMapper.Runtime;

namespace MyAutoMapper.UnitTests.Runtime;

public class MapperTests
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

    private readonly IMapper _mapper;

    public MapperTests()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Map_SimpleProperties_MapsCorrectly()
    {
        var source = new SimpleSource { Id = 1, Name = "Test", Price = 9.99m };
        var dest = _mapper.Map<SimpleSource, SimpleDest>(source);

        dest.Id.Should().Be(1);
        dest.Name.Should().Be("Test");
        dest.Price.Should().Be(9.99m);
    }

    [Fact]
    public void Map_NullSource_ReturnsDefault()
    {
        var dest = _mapper.Map<SimpleSource, SimpleDest>(null!);
        dest.Should().BeNull();
    }

    [Fact]
    public void Map_UnmappedPair_ThrowsInvalidOperation()
    {
        var act = () => _mapper.Map<SimpleDest, SimpleSource>(new SimpleDest());
        act.Should().Throw<InvalidOperationException>();
    }
}
