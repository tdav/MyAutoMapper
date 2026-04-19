using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Internal;
using SmAutoMapper.Runtime;
using SmAutoMapper.Validation;

namespace SmAutoMapper.Extensions;

public static class ServiceCollectionExtensions
{
    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure = null,
        params Assembly[] profileAssemblies)
        => AddMappingCore(services, configure, validatorLoggerFactory: null, profileAssemblies);

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure,
        ILoggerFactory? validatorLoggerFactory,
        params Assembly[] profileAssemblies)
        => AddMappingCore(services, configure, validatorLoggerFactory, profileAssemblies);

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        params Assembly[] profileAssemblies)
        => AddMappingCore(services, configure: null, validatorLoggerFactory: null, profileAssemblies);

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
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
