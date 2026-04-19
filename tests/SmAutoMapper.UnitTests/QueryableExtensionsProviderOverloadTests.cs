using FluentAssertions;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;
using Xunit;

namespace SmAutoMapper.UnitTests;

public sealed class QueryableExtensionsProviderOverloadTests
{
    // Plain classes (not records): ProjectionCompiler uses Expression.New(type) — parameterless ctor required.
    private sealed class SourceEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class DestDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TestProfile : MappingProfile
    {
        public TestProfile()
        {
            CreateMap<SourceEntity, DestDto>();
        }
    }

    private static (IProjectionProvider Provider, IQueryable<SourceEntity> Source) Arrange()
    {
        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<TestProfile>();
        var config = builder.Build();
        var provider = config.CreateProjectionProvider();
        var source = new[]
        {
            new SourceEntity { Id = 1, Name = "a" },
            new SourceEntity { Id = 2, Name = "b" }
        }.AsQueryable();
        return (provider, source);
    }

    [Fact]
    public void ProjectTo_TDest_WithProvider_ProjectsCorrectly()
    {
        var (provider, source) = Arrange();

        var result = ((IQueryable)source).ProjectTo<DestDto>(provider).ToList();

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("a");
        result[1].Id.Should().Be(2);
        result[1].Name.Should().Be("b");
    }

    [Fact]
    public void ProjectTo_TDest_WithProviderAndParameters_ProjectsCorrectly()
    {
        var (provider, source) = Arrange();

        var result = ((IQueryable)source).ProjectTo<DestDto>(provider, _ => { }).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ProjectTo_TDest_WithProvider_ProducesSameResultAsLegacyOverload()
    {
        var (provider, source) = Arrange();
#pragma warning disable SMAM0001 // test calls legacy accessor to set up comparison baseline
        ProjectionProviderAccessor.SetInstance(provider);
#pragma warning restore SMAM0001

#pragma warning disable SMAM0002 // legacy comparison target
        var legacy = ((IQueryable)source).ProjectTo<DestDto>().ToList();
#pragma warning restore SMAM0002
        var viaProvider = ((IQueryable)source).ProjectTo<DestDto>(provider).ToList();

        viaProvider.Should().HaveCount(legacy.Count);
        for (int i = 0; i < legacy.Count; i++)
        {
            viaProvider[i].Id.Should().Be(legacy[i].Id);
            viaProvider[i].Name.Should().Be(legacy[i].Name);
        }
    }
}
