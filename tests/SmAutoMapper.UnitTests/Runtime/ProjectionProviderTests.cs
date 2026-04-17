using FluentAssertions;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;
using SmAutoMapper.Configuration;
using SmAutoMapper.Compilation;
using SmAutoMapper.Extensions;

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
                .ForMember(d => d.LocalizedName, o => o.MapFrom<string>(lang,
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

public class ExplicitCollectionMapFromProjectionTests
{
    private sealed class Node
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<Node> Children { get; set; } = new();
    }

    private sealed class NodeVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<NodeVm> Children { get; set; } = new();
    }

    private sealed class Profile : MappingProfile
    {
        public Profile()
        {
            CreateMap<Node, NodeVm>()
                .MaxDepth(3)
                .ForMember(d => d.Children, o => o.MapFrom(s => s.Children));
        }
    }

    [Fact]
    public void Explicit_collection_MapFrom_projects_children()
    {
        var cfg = new MapperConfiguration(new[] { new Profile() });
        var provider = cfg.CreateProjectionProvider();

        var data = new[]
        {
            new Node
            {
                Id = 1, Name = "root",
                Children =
                {
                    new Node { Id = 2, Name = "child" }
                }
            }
        }.AsQueryable();

        var projected = data.ProjectTo<Node, NodeVm>(provider).Single();

        projected.Id.Should().Be(1);
        projected.Children.Should().HaveCount(1);
        projected.Children[0].Id.Should().Be(2);
    }
}

public class ExplicitCollectionMapFromFailFastTests
{
    private sealed class Src { public List<Inner> Items { get; set; } = new(); }
    private sealed class Dst { public List<InnerVm> Items { get; set; } = new(); }
    private sealed class Inner { public int X { get; set; } }
    private sealed class InnerVm { public int X { get; set; } }

    private sealed class MissingElementMapProfile : MappingProfile
    {
        public MissingElementMapProfile()
        {
            // NOTE: no CreateMap<Inner, InnerVm>() — intentional.
            CreateMap<Src, Dst>()
                .ForMember(d => d.Items, o => o.MapFrom(s => s.Items));
        }
    }

    [Fact]
    public void Explicit_without_element_TypeMap_throws_with_clear_message()
    {
        Action act = () => new MapperConfiguration(new[] { new MissingElementMapProfile() });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Items*Inner*InnerVm*");
    }
}

public class ConventionCollectionWithoutElementMapIsSilentTests
{
    private sealed class Src { public List<Inner> Items { get; set; } = new(); }
    private sealed class Dst { public List<InnerVm> Items { get; set; } = new(); }
    private sealed class Inner { public int X { get; set; } }
    private sealed class InnerVm { public int X { get; set; } }

    private sealed class ConventionOnlyProfile : MappingProfile
    {
        public ConventionOnlyProfile() => CreateMap<Src, Dst>();
    }

    [Fact]
    public void Convention_without_element_TypeMap_does_not_throw()
    {
        Action act = () => new MapperConfiguration(new[] { new ConventionOnlyProfile() });
        act.Should().NotThrow();
    }
}
