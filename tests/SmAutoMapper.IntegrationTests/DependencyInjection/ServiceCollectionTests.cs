using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmAutoMapper.Compilation;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.IntegrationTests.DependencyInjection;

public class ServiceCollectionTests
{
    [Fact]
    public void AddMapping_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddMapping(typeof(EfCore.ProductProfile).Assembly);

        var provider = services.BuildServiceProvider();

        provider.GetService<MapperConfiguration>().Should().NotBeNull();
        provider.GetService<IMapper>().Should().NotBeNull();
        provider.GetService<IProjectionProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddMapping_RegistersAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddMapping(typeof(EfCore.ProductProfile).Assembly);

        var provider = services.BuildServiceProvider();

        var config1 = provider.GetService<MapperConfiguration>();
        var config2 = provider.GetService<MapperConfiguration>();
        config1.Should().BeSameAs(config2);
    }
}
