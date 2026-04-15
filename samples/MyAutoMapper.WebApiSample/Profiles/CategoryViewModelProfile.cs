using MyAutoMapper.WebApiSample.Entities;
using MyAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.WebApiSample.Profiles;

public class CategoryViewModelProfile : MappingProfile
{
    public CategoryViewModelProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Category, CategoryViewModel>()
            .MaxDepth(5)
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu));
        // Children property is projected automatically via convention matching (same name as Category.Children)
    }
}
