using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    // Guards against ASP0000 regression: if AddMapping ever reintroduces services.BuildServiceProvider()
    // to resolve ILoggerFactory (the common case before PR #3), the sentinel factory below would be invoked
    // during AddMapping registration, tripping the counter. Using an explicitly-registered ILoggerFactory
    // with a counting factory gives us an observable probe that fails loudly on regression.
    [Fact]
    public void AddMapping_DoesNotResolveILoggerFactoryDuringRegistration()
    {
        var services = new ServiceCollection();
        var loggerFactoryResolveCount = 0;
        services.AddSingleton<ILoggerFactory>(_ =>
        {
            loggerFactoryResolveCount++;
            return NullLoggerFactory.Instance;
        });

        services.AddMapping(cfg => cfg.AddProfile<SrcDstProfile>());

        loggerFactoryResolveCount.Should().Be(0,
            "AddMapping must not call services.BuildServiceProvider() to resolve ILoggerFactory (ASP0000 guard)");
    }
}
