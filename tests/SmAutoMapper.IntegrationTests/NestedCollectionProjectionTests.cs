using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.IntegrationTests;

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

    [Fact(Skip = "holder-sharing not yet implemented — nested levels create fresh holder (see ProjectionCompiler TODO)")]
    public void Parameter_lang_propagates_to_all_nesting_levels()
    {
        // TODO: This test documents the EXPECTED behavior once holder-sharing is implemented
        // in ProjectionCompiler. Currently, when a root projection holder receives a parameter
        // (e.g. lang = "ru"), child/nested projections always receive a fresh holder and do NOT
        // inherit the parameter value — so localized names at nested levels would be wrong.

        // EXPECTED setup (to be implemented when holder-sharing lands):
        //   1. Define LocalizedCategory entity with NameRu / NameUz properties (and Children).
        //   2. Define LocalizedCategoryVm with LocalizedName / Children.
        //   3. Define a Profile with ParameterSlot<string>("lang") and a resolver that picks
        //      NameRu vs NameUz based on the slot value.
        //   4. Build a 2-level tree (root → child) in the in-memory DB.
        //   5. ProjectTo<LocalizedCategory, LocalizedCategoryVm>(_proj, p => p.Set("lang", "ru"))
        //   6. Assert vm.LocalizedName == root.NameRu  (root level picks "ru")
        //   7. Assert vm.Children[0].LocalizedName == child.NameRu  (child level ALSO picks "ru")
        //      — this assertion is what will fail until holder-sharing is implemented.

        throw new NotImplementedException("Implement after holder-sharing is added to ProjectionCompiler.");
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
