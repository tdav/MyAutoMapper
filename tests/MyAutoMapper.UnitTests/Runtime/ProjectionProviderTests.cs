using FluentAssertions;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.UnitTests.Runtime;

public class ProjectionProviderTests
{
    private class SimpleProfile : MappingProfile
    {
        public SimpleProfile()
        {
            CreateMap<SimpleSource, SimpleDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.Price, o => o.MapFrom(s => s.Price));
        }
    }

    private class ParameterizedProfile : MappingProfile
    {
        public ParameterizedProfile()
        {
            var lang = DeclareParameter<string>("lang");

            CreateMap<LocalizedSource, LocalizedDest>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id))
                .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                    (s, l) => l == "en" ? s.NameEn :
                              l == "fr" ? s.NameFr :
                              s.NameDefault));
        }
    }

    [Fact]
    public void GetProjection_ReturnsValidExpression()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<SimpleProfile>();
        var config = builder.Build();
        var provider = config.CreateProjectionProvider();

        var expr = provider.GetProjection<SimpleSource, SimpleDest>();
        expr.Should().NotBeNull();

        // Compile and test the expression
        var func = expr.Compile();
        var source = new SimpleSource { Id = 1, Name = "Test", Price = 9.99m };
        var result = func(source);
        result.Id.Should().Be(1);
        result.Name.Should().Be("Test");
    }

    [Fact]
    public void GetProjection_WithParameters_InjectsValues()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ParameterizedProfile>();
        var config = builder.Build();
        var provider = config.CreateProjectionProvider();

        var binder = new ParameterBinder();
        binder.Set("lang", "en");
        var expr = provider.GetProjection<LocalizedSource, LocalizedDest>(binder);

        var func = expr.Compile();
        var source = new LocalizedSource { Id = 1, NameEn = "Hello", NameFr = "Bonjour", NameDefault = "Default" };
        var result = func(source);
        result.LocalizedName.Should().Be("Hello");
    }

    [Fact]
    public void GetProjection_WithDifferentParams_ReturnsDifferentResults()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<ParameterizedProfile>();
        var config = builder.Build();
        var provider = config.CreateProjectionProvider();

        // English
        var binderEn = new ParameterBinder();
        binderEn.Set("lang", "en");
        var exprEn = provider.GetProjection<LocalizedSource, LocalizedDest>(binderEn);
        var funcEn = exprEn.Compile();

        // French
        var binderFr = new ParameterBinder();
        binderFr.Set("lang", "fr");
        var exprFr = provider.GetProjection<LocalizedSource, LocalizedDest>(binderFr);
        var funcFr = exprFr.Compile();

        var source = new LocalizedSource { Id = 1, NameEn = "Hello", NameFr = "Bonjour", NameDefault = "Default" };
        funcEn(source).LocalizedName.Should().Be("Hello");
        funcFr(source).LocalizedName.Should().Be("Bonjour");
    }
}
