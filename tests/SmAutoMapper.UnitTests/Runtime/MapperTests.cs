using FluentAssertions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;

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

public class MapperExplicitCollectionMapFromTests
{
    private sealed class Src
    {
        public int Id { get; set; }
        public List<Src> Children { get; set; } = new();
    }

    private sealed class Dst
    {
        public int Id { get; set; }
        public List<Dst> Children { get; set; } = new();
    }

    private sealed class Profile : MappingProfile
    {
        public Profile()
        {
            CreateMap<Src, Dst>()
                .MaxDepth(2)
                .ForMember(d => d.Children, o => o.MapFrom(s => s.Children));
        }
    }

    [Fact]
    public void Map_runs_through_explicit_collection_MapFrom()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile(new Profile());
        var cfg = builder.Build();
        var mapper = cfg.CreateMapper();

        var src = new Src
        {
            Id = 1,
            Children = { new Src { Id = 2, Children = { new Src { Id = 3 } } } }
        };

        var dst = mapper.Map<Src, Dst>(src);

        dst.Id.Should().Be(1);
        dst.Children.Should().HaveCount(1);
        dst.Children[0].Id.Should().Be(2);
    }
}
