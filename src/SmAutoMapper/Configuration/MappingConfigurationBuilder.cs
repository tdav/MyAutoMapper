using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SmAutoMapper.Compilation;

namespace SmAutoMapper.Configuration;

public sealed class MappingConfigurationBuilder
{
    private readonly List<MappingProfile> _profiles = [];

    public MappingConfigurationBuilder AddProfile(MappingProfile profile)
    {
        _profiles.Add(profile);
        return this;
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public MappingConfigurationBuilder AddProfile<TProfile>() where TProfile : MappingProfile, new()
    {
        _profiles.Add(new TProfile());
        return this;
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public MappingConfigurationBuilder AddProfile(Type profileType)
    {
        if (!typeof(MappingProfile).IsAssignableFrom(profileType) || profileType.IsAbstract)
            throw new ArgumentException($"Type '{profileType.Name}' is not a valid MappingProfile.");

        var profile = (MappingProfile)Activator.CreateInstance(profileType)!;
        _profiles.Add(profile);
        return this;
    }

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
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

    [RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
    [RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
    public MapperConfiguration Build()
    {
        return new MapperConfiguration(_profiles);
    }
}
