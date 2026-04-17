using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.IntegrationTests.EfCore;

public class ProjectToTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly IProjectionProvider _projections;

    public ProjectToTests()
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
        builder.AddProfile<ProductProfile>();
        var config = builder.Build();
        _projections = config.CreateProjectionProvider();
    }

    [Fact]
    public async Task ProjectTo_BasicMapping_ReturnsCorrectResults()
    {
        var results = await _context.Products
            .ProjectTo<Product, ProductDto>(_projections)
            .OrderBy(p => p.Id)
            .ToListAsync();

        results.Should().HaveCount(2);
        results[0].Id.Should().Be(1);
        results[0].Name.Should().Be("Widget");
        results[0].Price.Should().Be(9.99m);
        results[1].Id.Should().Be(2);
        results[1].Name.Should().Be("Gizmo");
        results[1].Price.Should().Be(19.99m);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
