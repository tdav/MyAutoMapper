using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;
using SmAutoMapper.Validation;

namespace SmAutoMapper.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure = null,
        params Assembly[] profileAssemblies)
    {
        var builder = new MappingConfigurationBuilder();
        configure?.Invoke(builder);

        foreach (var assembly in profileAssemblies)
        {
            builder.AddProfiles(assembly);
        }

        var configuration = builder.Build();

        // Validate all mappings eagerly
        using var tempSp = services.BuildServiceProvider();
        var logger = tempSp.GetService<ILoggerFactory>()?.CreateLogger<ConfigurationValidator>();
        var validator = new ConfigurationValidator(logger);
        validator.Validate(configuration.GetAllTypeMaps(), configuration.GetAllTypeMapConfigurations());

        // Register as singletons
        services.AddSingleton(configuration);
        services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().CreateMapper());
        var projectionProvider = configuration.CreateProjectionProvider();
        ProjectionProviderAccessor.SetInstance(projectionProvider);
        services.AddSingleton<IProjectionProvider>(projectionProvider);

        return services;
    }

    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        params Assembly[] profileAssemblies)
    {
        return AddMapping(services, null, profileAssemblies);
    }
}
