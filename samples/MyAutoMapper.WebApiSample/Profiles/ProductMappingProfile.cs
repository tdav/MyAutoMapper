using MyAutoMapper.WebApiSample.Entities;
using MyAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.WebApiSample.Profiles;

/// <summary>
/// Parameterized projection: Product → ProductViewModel.
/// Uses ParameterSlot&lt;string&gt;("lang") to select localized Name and Description
/// at query time. EF Core translates this to SQL parameters (@__lang_0).
/// </summary>
public class ProductMappingProfile : MappingProfile
{
    public ProductMappingProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Product, ProductViewModel>()
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu))
            .ForMember(d => d.LocalizedDescription, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.DescriptionUz
                          : l == "lt" ? src.DescriptionLt
                          : src.DescriptionRu));
    }
}
