using MyAutoMapper.Parameters;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.IntegrationTests.EfCore;

public class ProductProfile : MappingProfile
{
    public ProductProfile()
    {
        CreateMap<Product, ProductDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.NameEn))
            .ForMember(d => d.Price, o => o.MapFrom(s => s.Price));
    }
}

public class LocalizedProductProfile : MappingProfile
{
    public LocalizedProductProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Product, ProductLocalizedDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "en" ? src.NameEn :
                            l == "fr" ? src.NameFr :
                            src.NameDefault))
            .ForMember(d => d.Price, o => o.MapFrom(s => s.Price));
    }
}
