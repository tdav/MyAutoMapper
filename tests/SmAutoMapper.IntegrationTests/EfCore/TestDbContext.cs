using Microsoft.EntityFrameworkCore;

namespace MyAutoMapper.IntegrationTests.EfCore;

public class TestDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.NameEn).HasMaxLength(200);
            e.Property(p => p.NameFr).HasMaxLength(200);
            e.Property(p => p.NameDefault).HasMaxLength(200);
        });
    }
}

public class Product
{
    public int Id { get; set; }
    public string NameEn { get; set; } = "";
    public string NameFr { get; set; } = "";
    public string NameDefault { get; set; } = "";
    public decimal Price { get; set; }
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class ProductLocalizedDto
{
    public int Id { get; set; }
    public string LocalizedName { get; set; } = "";
    public decimal Price { get; set; }
}
