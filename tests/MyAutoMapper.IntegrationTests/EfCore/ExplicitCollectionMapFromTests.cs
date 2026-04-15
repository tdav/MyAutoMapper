using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Configuration;
using SmAutoMapper.Runtime;
using SmAutoMapper.Extensions;

namespace MyAutoMapper.IntegrationTests.EfCore;

public class ExplicitCollectionMapFromTests : IDisposable
{
    private sealed class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<Category> Children { get; set; } = new();
    }

    private sealed class CategoryVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<CategoryVm> Children { get; set; } = new();
    }

    private sealed class Ctx : DbContext
    {
        public Ctx(DbContextOptions<Ctx> options) : base(options) { }
        public DbSet<Category> Categories => Set<Category>();
        protected override void OnModelCreating(ModelBuilder b)
            => b.Entity<Category>()
                .HasMany(c => c.Children)
                .WithOne()
                .HasForeignKey(c => c.ParentId);
    }

    private sealed class Profile : MappingProfile
    {
        public Profile()
        {
            CreateMap<Category, CategoryVm>()
                .MaxDepth(3)
                .ForMember(d => d.Children, o => o.MapFrom(s => s.Children));
        }
    }

    private readonly Ctx _ctx;
    private readonly IProjectionProvider _projections;

    public ExplicitCollectionMapFromTests()
    {
        var opts = new DbContextOptionsBuilder<Ctx>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new Ctx(opts);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();

        var root = new Category
        {
            Id = 1, Name = "root",
            Children =
            {
                new Category { Id = 2, Name = "L1",
                    Children = { new Category { Id = 3, Name = "L2" } } }
            }
        };
        _ctx.Categories.Add(root);
        _ctx.SaveChanges();

        var builder = new MappingConfigurationBuilder();
        builder.AddProfile(new Profile());
        var cfg = builder.Build();
        _projections = cfg.CreateProjectionProvider();
    }

    [Fact]
    public void Explicit_MapFrom_on_Children_projects_via_EF()
    {
        var vm = _ctx.Categories
            .Where(c => c.Id == 1)
            .ProjectTo<Category, CategoryVm>(_projections)
            .Single();

        vm.Name.Should().Be("root");
        vm.Children.Should().HaveCount(1);
        vm.Children[0].Name.Should().Be("L1");
        vm.Children[0].Children.Should().HaveCount(1);
        vm.Children[0].Children[0].Name.Should().Be("L2");
    }

    public void Dispose()
    {
        _ctx.Database.CloseConnection();
        _ctx.Dispose();
    }
}
