using SmAutoMapper.WebApiSample.Entities;
using SmAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Configuration;

namespace SmAutoMapper.WebApiSample.Profiles;

public class CategoryViewModelProfile : MappingProfile
{
    public CategoryViewModelProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Category, CategoryViewModel>()
            .MaxDepth(5)
            .ForMember(d => d.LocalizedName, o => o.MapFrom<string>(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu))
            .ForMember(d => d.Children, o => o.MapFrom(src => src.Children));
    }
}
