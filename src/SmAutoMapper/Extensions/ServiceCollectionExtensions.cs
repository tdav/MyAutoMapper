using System.Diagnostics.CodeAnalysis;
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
    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure = null,
        params Assembly[] profileAssemblies)
        => AddMappingCore(services, configure, validatorLoggerFactory: null, profileAssemblies);

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure,
        ILoggerFactory? validatorLoggerFactory,
        params Assembly[] profileAssemblies)
        => AddMappingCore(services, configure, validatorLoggerFactory, profileAssemblies);

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        params Assembly[] profileAssemblies)
        => AddMappingCore(services, configure: null, validatorLoggerFactory: null, profileAssemblies);

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    private static IServiceCollection AddMappingCore(
        IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure,
        ILoggerFactory? validatorLoggerFactory,
        Assembly[] profileAssemblies)
    {
        var builder = new MappingConfigurationBuilder();
        configure?.Invoke(builder);

        foreach (var assembly in profileAssemblies)
        {
            builder.AddProfiles(assembly);
        }

        var configuration = builder.Build();

        var logger = validatorLoggerFactory?.CreateLogger<ConfigurationValidator>();
        var validator = new ConfigurationValidator(logger);
        validator.Validate(configuration.GetAllTypeMaps(), configuration.GetAllTypeMapConfigurations());

        services.AddSingleton(configuration);
        services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().CreateMapper());
        var projectionProvider = configuration.CreateProjectionProvider();

#pragma warning disable SMAM0001 // populate legacy accessor for 1.x consumers still using single-generic ProjectTo
        ProjectionProviderAccessor.SetInstance(projectionProvider);
#pragma warning restore SMAM0001

        services.AddSingleton<IProjectionProvider>(projectionProvider);

        return services;
    }
}
