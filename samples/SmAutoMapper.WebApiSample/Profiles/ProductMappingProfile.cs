using SmAutoMapper.WebApiSample.Entities;
using SmAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Configuration;

namespace SmAutoMapper.WebApiSample.Profiles;

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
            .ForMember(d => d.LocalizedName, o => o.MapFrom<string>(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu))
            .ForMember(d => d.LocalizedDescription, o => o.MapFrom<string>(lang,
                (src, l) => l == "uz" ? src.DescriptionUz
                          : l == "lt" ? src.DescriptionLt
                          : src.DescriptionRu));
    }
}
