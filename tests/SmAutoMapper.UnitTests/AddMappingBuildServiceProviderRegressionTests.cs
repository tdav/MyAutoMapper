using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;
using Xunit;

namespace SmAutoMapper.UnitTests;

public sealed class AddMappingBuildServiceProviderRegressionTests
{
    private sealed class Src { public int Id { get; set; } }
    private sealed class Dst { public int Id { get; set; } }

    private sealed class SrcDstProfile : MappingProfile
    {
        public SrcDstProfile() => CreateMap<Src, Dst>();
    }

    [Fact]
    public void AddMapping_ResolvesProjectionProvider_WithoutBuildingTempProvider()
    {
        var services = new ServiceCollection();
        services.AddMapping(cfg => cfg.AddProfile<SrcDstProfile>());
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IProjectionProvider>();

        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddMapping_NoOptionalLogger_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMapping(cfg => cfg.AddProfile<SrcDstProfile>());

        act.Should().NotThrow();
    }
}
