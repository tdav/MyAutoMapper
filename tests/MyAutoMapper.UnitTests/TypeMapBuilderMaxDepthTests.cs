using FluentAssertions;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.UnitTests;

public class TypeMapBuilderMaxDepthTests
{
    private sealed class Src { public List<Src> Children { get; set; } = []; }
    private sealed class Dst { public List<Dst> Children { get; set; } = []; }

    private sealed class TestProfile : MappingProfile
    {
        public TestProfile() => CreateMap<Src, Dst>().MaxDepth(5);
    }

    [Fact]
    public void MaxDepth_is_stored_on_configuration()
    {
        var profile = new TestProfile();
        var config = profile.TypeMaps.Single();
        config.MaxDepth.Should().Be(5);
    }

    [Fact]
    public void MaxDepth_defaults_to_null_when_not_set()
    {
        var p = new MappingProfileAnon(b => b.CreateMap<Src, Dst>());
        p.TypeMaps.Single().MaxDepth.Should().BeNull();
    }

    private sealed class MappingProfileAnon : MappingProfile
    {
        public MappingProfileAnon(Action<MappingProfileAnon> cfg) => cfg(this);
        public new ITypeMappingExpression<TS, TD> CreateMap<TS, TD>() => base.CreateMap<TS, TD>();
    }
}
