using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentAssertions;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;

namespace SmAutoMapper.UnitTests;

// Guards the 1.1.0 AOT contract: every public entry point that can trigger runtime codegen
// MUST advertise RequiresDynamicCode + RequiresUnreferencedCode so AOT/trim analyzers flag callers.
public sealed class PublicApiAotAttributesTests
{
    public static TheoryData<Type, string> AttributedMethods() => new()
    {
        // AddMapping (all overloads)
        { typeof(ServiceCollectionExtensions), nameof(ServiceCollectionExtensions.AddMapping) },

        // ProjectTo (all overloads)
        { typeof(QueryableExtensions), nameof(QueryableExtensions.ProjectTo) },

        // Builder entry points
        { typeof(MappingConfigurationBuilder), nameof(MappingConfigurationBuilder.Build) },
        { typeof(MapperConfiguration), nameof(MapperConfiguration.CreateMapper) },
        { typeof(MapperConfiguration), nameof(MapperConfiguration.CreateProjectionProvider) },
    };

    [Theory]
    [MemberData(nameof(AttributedMethods))]
    public void PublicApi_HasRequiresDynamicCode_AndRequiresUnreferencedCode(Type type, string methodName)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(m => m.Name == methodName && m.DeclaringType == type)
            .ToArray();

        methods.Should().NotBeEmpty($"{type.Name}.{methodName} should exist");
        foreach (var m in methods)
        {
            var signature = $"{type.Name}.{methodName}({string.Join(',', m.GetParameters().Select(p => p.ParameterType.Name))})";
            m.GetCustomAttribute<RequiresDynamicCodeAttribute>()
                .Should().NotBeNull($"{signature} must carry [RequiresDynamicCode]");
            m.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()
                .Should().NotBeNull($"{signature} must carry [RequiresUnreferencedCode]");
        }
    }

    // MappingProfile.CreateMap is protected, so reach it via non-public instance binding.
    [Fact]
    public void MappingProfile_CreateMap_HasAotAttributes()
    {
        var methods = typeof(MappingProfile)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => m.Name == "CreateMap" && m.DeclaringType == typeof(MappingProfile))
            .ToArray();

        methods.Should().NotBeEmpty("MappingProfile.CreateMap should exist");
        foreach (var m in methods)
        {
            m.GetCustomAttribute<RequiresDynamicCodeAttribute>()
                .Should().NotBeNull("MappingProfile.CreateMap must carry [RequiresDynamicCode]");
            m.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()
                .Should().NotBeNull("MappingProfile.CreateMap must carry [RequiresUnreferencedCode]");
        }
    }
}
