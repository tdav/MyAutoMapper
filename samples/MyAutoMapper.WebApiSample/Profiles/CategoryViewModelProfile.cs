using MyAutoMapper.WebApiSample.Entities;
using MyAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.WebApiSample.Profiles;

/// <summary>
/// Parameterized projection: Category → CategoryViewModel.
/// Uses ParameterSlot&lt;string&gt;("lang") to select localized name.
/// SubCategories is ignored in the projection (built in memory after query).
/// </summary>
public class CategoryViewModelProfile : MappingProfile
{
    public CategoryViewModelProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Category, CategoryViewModel>()
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu))
            .ForMember(d => d.SubCategories, o => o.Ignore());
    }
}
