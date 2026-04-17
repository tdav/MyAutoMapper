using FluentAssertions;
using SmAutoMapper.Configuration;

namespace SmAutoMapper.UnitTests.Conventions;

public class ConventionMappingTests
{
    private class ConventionProfile : MappingProfile
    {
        public ConventionProfile()
        {
            // ForMember with no MapFrom triggers convention-based matching (same-name properties)
            CreateMap<SimpleSource, SimpleDest>()
                .ForMember(d => d.Id, o => { })
                .ForMember(d => d.Name, o => { })
                .ForMember(d => d.Price, o => { });
        }
    }

    private class FlatteningProfile : MappingProfile
    {
        public FlatteningProfile()
        {
            // ForMember with no MapFrom triggers convention-based matching (flattening)
            CreateMap<SourceWithNested, FlatDest>()
                .ForMember(d => d.Id, o => { })
                .ForMember(d => d.AddressCity, o => { })
                .ForMember(d => d.AddressStreet, o => { });
        }
    }

    private class ReverseMapProfile : MappingProfile
    {
        public ReverseMapProfile()
        {
            CreateMap<SimpleSource, SimpleDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.Price, o => o.MapFrom(s => s.Price))
                .ReverseMap();
        }
    }

    [Fact]
    public void Convention_SameNameProperties_AutoMapped()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ConventionProfile>();
        var config = builder.Build();
        var mapper = config.CreateMapper();

        var source = new SimpleSource { Id = 42, Name = "Conv", Price = 3.14m };
        var dest = mapper.Map<SimpleSource, SimpleDest>(source);

        dest.Id.Should().Be(42);
        dest.Name.Should().Be("Conv");
        dest.Price.Should().Be(3.14m);
    }

    [Fact]
    public void Flattening_NestedProperties_AutoMapped()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<FlatteningProfile>();
        var config = builder.Build();
        var mapper = config.CreateMapper();

        var source = new SourceWithNested
        {
            Id = 1,
            Address = new Address { City = "NYC", Street = "Broadway" }
        };
        var dest = mapper.Map<SourceWithNested, FlatDest>(source);

        dest.Id.Should().Be(1);
        dest.AddressCity.Should().Be("NYC");
        dest.AddressStreet.Should().Be("Broadway");
    }

    [Fact]
    public void Flattening_NullIntermediateProperty_ReturnsDefault()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<FlatteningProfile>();
        var config = builder.Build();
        var mapper = config.CreateMapper();

        var source = new SourceWithNested
        {
            Id = 2,
            Address = null!
        };
        var dest = mapper.Map<SourceWithNested, FlatDest>(source);

        dest.Id.Should().Be(2);
        dest.AddressCity.Should().BeNull();
        dest.AddressStreet.Should().BeNull();
    }

    [Fact]
    public void ReverseMap_CreatesReverseMapping()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ReverseMapProfile>();
        var config = builder.Build();
        var mapper = config.CreateMapper();

        // Forward
        var source = new SimpleSource { Id = 1, Name = "Test", Price = 9.99m };
        var dest = mapper.Map<SimpleSource, SimpleDest>(source);
        dest.Name.Should().Be("Test");

        // Reverse
        var reversed = mapper.Map<SimpleDest, SimpleSource>(dest);
        reversed.Id.Should().Be(1);
        reversed.Name.Should().Be("Test");
        reversed.Price.Should().Be(9.99m);
    }
}
