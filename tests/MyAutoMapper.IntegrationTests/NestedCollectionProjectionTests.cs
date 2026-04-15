using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace MyAutoMapper.IntegrationTests;

public class NestedCollectionProjectionTests : IDisposable
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public List<Category> Children { get; set; } = [];
    }

    public class CategoryVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<CategoryVm> Children { get; set; } = [];
    }

    public class Db : DbContext
    {
        public Db(DbContextOptions<Db> opt) : base(opt) { }
        public DbSet<Category> Categories => Set<Category>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Category>().HasMany(c => c.Children).WithOne().HasForeignKey(c => c.ParentId);
        }
    }

    private sealed class Profile : MappingProfile
    {
        public Profile()
        {
            CreateMap<Category, CategoryVm>()
                .MaxDepth(3);
        }
    }

    private readonly Db _db;
    private readonly IProjectionProvider _proj;

    public NestedCollectionProjectionTests()
    {
        var opts = new DbContextOptionsBuilder<Db>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new Db(opts);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        var root = new Category
        {
            Id = 1, Name = "root",
            Children =
            {
                new Category
                {
                    Id = 2, Name = "L1",
                    Children =
                    {
                        new Category
                        {
                            Id = 3, Name = "L2",
                            Children =
                            {
                                new Category
                                {
                                    Id = 4, Name = "L3",
                                    Children =
                                    {
                                        new Category { Id = 5, Name = "L4" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        _db.Categories.Add(root);
        _db.SaveChanges();

        var cfg = new MappingConfigurationBuilder()
            .AddProfile(new Profile())
            .Build();
        _proj = cfg.CreateProjectionProvider();
    }

    [Fact]
    public void Projects_up_to_MaxDepth_levels()
    {
        var vm = _db.Categories.Where(c => c.Id == 1)
            .ProjectTo<Category, CategoryVm>(_proj)
            .Single();

        vm.Name.Should().Be("root");
        vm.Children.Should().HaveCount(1);
        vm.Children[0].Name.Should().Be("L1");
        vm.Children[0].Children.Should().HaveCount(1);
        vm.Children[0].Children[0].Name.Should().Be("L2");
        // Depth 3: root → L1 → L2 filled; L2.Children should be empty
        vm.Children[0].Children[0].Children.Should().BeEmpty();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}
