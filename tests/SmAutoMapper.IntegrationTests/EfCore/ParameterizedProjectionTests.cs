using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.IntegrationTests.EfCore;

public class ParameterizedProjectionTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly IProjectionProvider _projections;

    public ParameterizedProjectionTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new TestDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _context.Products.AddRange(
            new Product { Id = 1, NameEn = "Widget", NameFr = "Gadget", NameDefault = "Widget_D", Price = 9.99m },
            new Product { Id = 2, NameEn = "Gizmo", NameFr = "Bidule", NameDefault = "Gizmo_D", Price = 19.99m }
        );
        _context.SaveChanges();

        var builder = new MappingConfigurationBuilder();
        builder.AddProfile<LocalizedProductProfile>();
        var config = builder.Build();
        _projections = config.CreateProjectionProvider();
    }

    [Fact]
    public async Task ProjectTo_WithLangEn_ReturnsEnglishNames()
    {
        var results = await _context.Products
            .ProjectTo<Product, ProductLocalizedDto>(_projections, p => p.Set("lang", "en"))
            .OrderBy(p => p.Id)
            .ToListAsync();

        results.Should().HaveCount(2);
        results[0].LocalizedName.Should().Be("Widget");
        results[1].LocalizedName.Should().Be("Gizmo");
    }

    [Fact]
    public async Task ProjectTo_WithLangFr_ReturnsFrenchNames()
    {
        var results = await _context.Products
            .ProjectTo<Product, ProductLocalizedDto>(_projections, p => p.Set("lang", "fr"))
            .OrderBy(p => p.Id)
            .ToListAsync();

        results.Should().HaveCount(2);
        results[0].LocalizedName.Should().Be("Gadget");
        results[1].LocalizedName.Should().Be("Bidule");
    }

    [Fact]
    public async Task ProjectTo_WithUnknownLang_ReturnsDefaultNames()
    {
        var results = await _context.Products
            .ProjectTo<Product, ProductLocalizedDto>(_projections, p => p.Set("lang", "de"))
            .OrderBy(p => p.Id)
            .ToListAsync();

        results.Should().HaveCount(2);
        results[0].LocalizedName.Should().Be("Widget_D");
        results[1].LocalizedName.Should().Be("Gizmo_D");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
