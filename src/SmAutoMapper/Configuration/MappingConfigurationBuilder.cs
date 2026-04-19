using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SmAutoMapper.Compilation;
using SmAutoMapper.Internal;

namespace SmAutoMapper.Configuration;

public sealed class MappingConfigurationBuilder
{
    private readonly List<MappingProfile> _profiles = [];

    public MappingConfigurationBuilder AddProfile(MappingProfile profile)
    {
        _profiles.Add(profile);
        return this;
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public MappingConfigurationBuilder AddProfile<TProfile>() where TProfile : MappingProfile, new()
    {
        _profiles.Add(new TProfile());
        return this;
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public MappingConfigurationBuilder AddProfile(Type profileType)
    {
        if (!typeof(MappingProfile).IsAssignableFrom(profileType) || profileType.IsAbstract)
            throw new ArgumentException($"Type '{profileType.Name}' is not a valid MappingProfile.");

        var profile = (MappingProfile)Activator.CreateInstance(profileType)!;
        _profiles.Add(profile);
        return this;
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public MappingConfigurationBuilder AddProfiles(Assembly assembly)
    {
        var profileTypes = assembly.GetTypes()
            .Where(t => typeof(MappingProfile).IsAssignableFrom(t)
                     && !t.IsAbstract
                     && t.DeclaringType is null);

        foreach (var type in profileTypes)
        {
            AddProfile(type);
        }

        return this;
    }

    internal IReadOnlyList<MappingProfile> Profiles => _profiles;

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public MapperConfiguration Build()
    {
        return new MapperConfiguration(_profiles);
    }
}
