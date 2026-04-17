using FluentAssertions;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.UnitTests.Configuration;

public class MappingProfileTests
{
    private class TestProfile : MappingProfile
    {
        public TestProfile()
        {
            CreateMap<SimpleSource, SimpleDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name));
        }
    }

    private class MultiMapProfile : MappingProfile
    {
        public MultiMapProfile()
        {
            CreateMap<SimpleSource, SimpleDest>();
            CreateMap<SimpleSource, PartialDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id));
        }
    }

    [Fact]
    public void Profile_CreateMap_AccumulatesTypeMaps()
    {
        var profile = new TestProfile();
        profile.TypeMaps.Should().HaveCount(1);
        profile.TypeMaps[0].SourceType.Should().Be(typeof(SimpleSource));
        profile.TypeMaps[0].DestinationType.Should().Be(typeof(SimpleDest));
    }

    [Fact]
    public void Profile_MultipleCreateMaps_AccumulatesAll()
    {
        var profile = new MultiMapProfile();
        profile.TypeMaps.Should().HaveCount(2);
    }

    [Fact]
    public void Profile_ForMember_AccumulatesPropertyMaps()
    {
        var profile = new TestProfile();
        profile.TypeMaps[0].PropertyMaps.Should().HaveCount(2);
    }
}
