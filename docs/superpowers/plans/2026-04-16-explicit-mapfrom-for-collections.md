# Explicit MapFrom for Heterogeneous Collections — Implementation Plan

> **For Claude:** REQUIRED: Use superpowers:subagent-driven-development to
> implement this plan. Fresh subagent per task + two-stage review. Subagents
> should prefer `mcp__plugin_serena_serena__find_symbol`,
> `mcp__plugin_serena_serena__get_symbols_overview`,
> `mcp__plugin_serena_serena__replace_symbol_body`, and
> `mcp__plugin_serena_serena__insert_after_symbol` for symbolic code edits —
> they are cheaper and safer than full-file Read/Edit for this codebase.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow explicit `ForMember(d => d.Children, o => o.MapFrom(src => src.Children))` for collections whose element types differ, working in both EF `ProjectTo` and in-memory `Mapper.Map`.

**Architecture:** Add two new generic overloads `MapFrom<TSourceMember>` to `IMemberOptions`. `PropertyMap` already stores an untyped `LambdaExpression`, so downstream pipeline needs no schema change. `ProjectionCompiler` already contains `TryBuildNestedCollection` logic; we only need to differentiate **explicit** vs **convention-sourced** expressions and throw `InvalidOperationException` in the explicit-without-element-TypeMap case (convention keeps its silent-skip behaviour). Runtime `IMapper.Map` uses the same compiled expression, so no separate path to touch.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, Microsoft.EntityFrameworkCore.Sqlite.

**Spec:** [docs/superpowers/specs/2026-04-16-explicit-mapfrom-for-collections-design.md](../specs/2026-04-16-explicit-mapfrom-for-collections-design.md)

---

## File Structure

| Path | Action | Responsibility |
|------|--------|----------------|
| `src/MyAutoMapper/Configuration/IMemberOptions.cs` | Modify | Add two new `MapFrom<TSourceMember>` overloads |
| `src/MyAutoMapper/Configuration/MemberMapBuilder.cs` | Modify | Implement the two new overloads |
| `src/MyAutoMapper/Compilation/ProjectionCompiler.cs` | Modify | Track explicit vs convention origin; throw when explicit collection mapping has no element TypeMap |
| `tests/MyAutoMapper.UnitTests/Configuration/MemberMapBuilderTests.cs` | Create | Unit tests for new overloads |
| `tests/MyAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs` | Modify | Explicit collection MapFrom + fail-fast tests |
| `tests/MyAutoMapper.UnitTests/Runtime/MapperTests.cs` | Modify | Runtime `Map()` over explicit collection MapFrom |
| `tests/MyAutoMapper.IntegrationTests/EfCore/ExplicitCollectionMapFromTests.cs` | Create | EF Core Sqlite test of explicit collection projection |
| `samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs` | Modify | Replace convention comment with explicit `ForMember(...MapFrom(src => src.Children))` |

---

## Chunk 1: Full Implementation

### Task 1: Add the two new overloads to `IMemberOptions`

**Files:**
- Modify: `src/MyAutoMapper/Configuration/IMemberOptions.cs`

- [ ] **Step 1: Insert the two new overloads**

Replace the body of `IMemberOptions<TSource, TDest, TMember>` with:

```csharp
using System.Linq.Expressions;
using SmAutoMapper.Parameters;

namespace SmAutoMapper.Configuration;

public interface IMemberOptions<TSource, TDest, TMember>
{
    void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

    void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression);

    /// <summary>
    /// Maps from a source expression whose return type differs from <typeparamref name="TMember"/>.
    /// Primary use case: collections where the element type differs
    /// (e.g. <c>List&lt;Category&gt;</c> -> <c>List&lt;CategoryViewModel&gt;</c>).
    /// Requires a registered <c>TypeMap</c> for the element pair; otherwise the configuration compiler throws.
    /// </summary>
    void MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> sourceExpression);

    /// <summary>
    /// Parameterised variant of <see cref="MapFrom{TSourceMember}(Expression{Func{TSource, TSourceMember}})"/>.
    /// </summary>
    void MapFrom<TSourceMember, TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TSourceMember>> sourceExpression);

    void Ignore();
}
```

- [ ] **Step 2: Verify the solution compiles**

Run: `dotnet build src/MyAutoMapper/SmAutoMapper.csproj -v q --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.
If it fails because `MemberMapBuilder` does not implement the new members — **do NOT fix yet**, continue to Task 2.

- [ ] **Step 3: Commit**

```bash
git add src/MyAutoMapper/Configuration/IMemberOptions.cs
git commit -m "feat(config): add IMemberOptions.MapFrom<TSourceMember> overloads"
```

---

### Task 2: Implement the new overloads in `MemberMapBuilder`

**Files:**
- Modify: `src/MyAutoMapper/Configuration/MemberMapBuilder.cs`

- [ ] **Step 1: Add the two method bodies**

Insert **before** the existing `Ignore()` method:

```csharp
    public void MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> sourceExpression)
        => SourceExpression = sourceExpression;

    public void MapFrom<TSourceMember, TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TSourceMember>> sourceExpression)
    {
        HasParameterizedSource = true;
        ParameterSlot = parameter;
        ParameterizedSourceExpression = sourceExpression;
    }
```

- [ ] **Step 2: Build the library project**

Run: `dotnet build src/MyAutoMapper/SmAutoMapper.csproj -v q --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/MyAutoMapper/Configuration/MemberMapBuilder.cs
git commit -m "feat(config): implement MemberMapBuilder.MapFrom<TSourceMember> overloads"
```

---

### Task 3: Unit tests for the two new overloads in `MemberMapBuilder`

**Files:**
- Create: `tests/MyAutoMapper.UnitTests/Configuration/MemberMapBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MyAutoMapper.UnitTests/Configuration/MemberMapBuilderTests.cs`:

```csharp
using System.Linq.Expressions;
using FluentAssertions;
using SmAutoMapper.Configuration;
using SmAutoMapper.Parameters;

namespace MyAutoMapper.UnitTests.Configuration;

public class MemberMapBuilderTests
{
    private sealed class Src { public List<int> Items { get; set; } = new(); }
    private sealed class Dst { public List<string> Items { get; set; } = new(); }

    private static MemberMapBuilder<Src, Dst, List<string>> NewBuilder() =>
        (MemberMapBuilder<Src, Dst, List<string>>)Activator.CreateInstance(
            typeof(MemberMapBuilder<,,>).MakeGenericType(typeof(Src), typeof(Dst), typeof(List<string>)),
            nonPublic: true)!;

    [Fact]
    public void MapFrom_with_different_member_type_stores_expression()
    {
        var builder = NewBuilder();
        Expression<Func<Src, List<int>>> expr = s => s.Items;

        builder.MapFrom(expr);

        builder.SourceExpression.Should().BeSameAs(expr);
        builder.IsIgnored.Should().BeFalse();
        builder.HasParameterizedSource.Should().BeFalse();
    }

    [Fact]
    public void Parameterised_MapFrom_with_different_member_type_stores_all_state()
    {
        var builder = NewBuilder();
        var slot = new ParameterSlot<string>("lang");
        Expression<Func<Src, string, List<int>>> expr = (s, _) => s.Items;

        builder.MapFrom(slot, expr);

        builder.HasParameterizedSource.Should().BeTrue();
        builder.ParameterSlot.Should().BeSameAs(slot);
        builder.ParameterizedSourceExpression.Should().BeSameAs(expr);
    }
}
```

Note: `MemberMapBuilder` is `internal`; the test project already has
`InternalsVisibleTo("MyAutoMapper.UnitTests")` — verify before running.

- [ ] **Step 2: Verify `InternalsVisibleTo`**

Run:

```bash
grep -n "InternalsVisibleTo" src/MyAutoMapper/*.csproj src/MyAutoMapper/**/*.cs 2>/dev/null
```

Expected: a line referencing `MyAutoMapper.UnitTests`. If missing, add to
`src/MyAutoMapper/SmAutoMapper.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="MyAutoMapper.UnitTests" />
</ItemGroup>
```

- [ ] **Step 3: Run the new tests**

Run: `dotnet test tests/MyAutoMapper.UnitTests/MyAutoMapper.UnitTests.csproj --filter FullyQualifiedName~MemberMapBuilderTests --nologo -v q`
Expected: 2 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/MyAutoMapper.UnitTests/Configuration/MemberMapBuilderTests.cs src/MyAutoMapper/SmAutoMapper.csproj
git commit -m "test(config): cover MemberMapBuilder.MapFrom<TSourceMember> overloads"
```

---

### Task 4: Failing test — explicit collection MapFrom with element TypeMap (projection)

**Files:**
- Modify: `tests/MyAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs`

Goal: prove that when the user writes `.ForMember(d => d.Children, o => o.MapFrom(s => s.Children))` and both `Category → CategoryViewModel` **and** that same map are registered, projection produces a `Select(c => new CategoryViewModel { ... })`.

- [ ] **Step 1: Append the new test cases**

Add the following types and tests at the bottom of
`tests/MyAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs` (inside
the same namespace):

```csharp
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

        var projected = data.ProjectTo<NodeVm>(provider).Single();

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
```

- [ ] **Step 2: Run to confirm the first two tests fail, the third passes**

Run: `dotnet test tests/MyAutoMapper.UnitTests/MyAutoMapper.UnitTests.csproj --filter "FullyQualifiedName~ExplicitCollectionMapFrom|FullyQualifiedName~ConventionCollectionWithoutElementMapIsSilent" --nologo -v q`

Expected:
- `Explicit_collection_MapFrom_projects_children` FAILS (either `CS0029`-like compile build fault or runtime `InvalidCastException`/null children depending on how far the compiler gets today — it should be red for a type-related reason).
- `Explicit_without_element_TypeMap_throws_with_clear_message` FAILS (no exception thrown; current code silently skips).
- `Convention_without_element_TypeMap_does_not_throw` PASSES (regression baseline).

- [ ] **Step 3: Commit the failing tests**

```bash
git add tests/MyAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs
git commit -m "test(projection): add red tests for explicit collection MapFrom"
```

---

### Task 5: Make `ProjectionCompiler` distinguish explicit vs convention and fail fast

**Files:**
- Modify: `src/MyAutoMapper/Compilation/ProjectionCompiler.cs`

- [ ] **Step 1: Locate the compile-time member loop**

Use Serena:

```text
mcp__plugin_serena_serena__find_symbol(
  name_path_pattern: "ProjectionCompiler/CompileProjection",
  relative_path: "src/MyAutoMapper/Compilation/ProjectionCompiler.cs",
  include_body: true)
```

Confirm the section around the original line 95-136 where
`propertyMap.SourceExpression is LambdaExpression sourceLambda` is handled
and the collection/convert fallback lives.

- [ ] **Step 2: Introduce the `isExplicit` flag and replace the skip**

Inside the loop, right before the block that assigns `valueExpression` from
either a parameterised source, an explicit `SourceExpression`, or a
convention, declare a flag and set it in the explicit branch. Then change
the fallback to throw when explicit.

Replacement block (keep the surrounding structure; apply edits surgically):

```csharp
// BEFORE: the three branches that fill valueExpression.
bool isExplicit = false;

if (propertyMap.HasParameterizedSource
    && propertyMap.ParameterizedSourceExpression is LambdaExpression paramLambda
    && propertyMap.ParameterSlot is not null)
{
    // ... existing body unchanged ...
    isExplicit = true;
}
else if (propertyMap.SourceExpression is LambdaExpression sourceLambda)
{
    valueExpression = ParameterReplacer.Replace(
        sourceLambda.Body,
        sourceLambda.Parameters[0],
        sourceParam);
    isExplicit = true;
}
else
{
    // convention branch unchanged
}
```

Then replace the mismatch handling:

```csharp
if (valueExpression.Type != propertyMap.DestinationProperty.PropertyType)
{
    if (TryBuildNestedCollection(
            valueExpression,
            propertyMap.DestinationProperty.PropertyType,
            catalog,
            compilationStack,
            sharedHolderConstant: holderConstant,
            sharedHolderInfo: holderInfo,
            out var nested))
    {
        valueExpression = nested!;
    }
    else if (CollectionProjectionBuilder.TryGetElementType(valueExpression.Type, out var srcElement)
          && CollectionProjectionBuilder.TryGetElementType(propertyMap.DestinationProperty.PropertyType, out var dstElement)
          && !propertyMap.DestinationProperty.PropertyType.IsAssignableFrom(valueExpression.Type))
    {
        if (isExplicit)
        {
            throw new InvalidOperationException(
                $"Cannot map member '{propertyMap.DestinationProperty.DeclaringType?.Name}.{propertyMap.DestinationProperty.Name}': " +
                $"no TypeMap registered to project elements '{srcElement.FullName}' -> '{dstElement.FullName}'. " +
                $"Register the map via CreateMap<{srcElement.Name}, {dstElement.Name}>() or remove the explicit ForMember.");
        }
        continue;
    }
    else
    {
        valueExpression = Expression.Convert(valueExpression, propertyMap.DestinationProperty.PropertyType);
    }
}
```

Preserve the logic of the two existing branches untouched except for the
`isExplicit = true` assignment and the new throw.

- [ ] **Step 3: Run the three tests added in Task 4**

Run: `dotnet test tests/MyAutoMapper.UnitTests/MyAutoMapper.UnitTests.csproj --filter "FullyQualifiedName~ExplicitCollectionMapFrom|FullyQualifiedName~ConventionCollectionWithoutElementMapIsSilent" --nologo -v q`

Expected: all three tests pass.

- [ ] **Step 4: Run the entire unit test project**

Run: `dotnet test tests/MyAutoMapper.UnitTests/MyAutoMapper.UnitTests.csproj --nologo -v q`
Expected: all tests pass (no regression in convention, MaxDepth, parameter, flattening, etc.).

- [ ] **Step 5: Commit**

```bash
git add src/MyAutoMapper/Compilation/ProjectionCompiler.cs
git commit -m "feat(compiler): fail fast when explicit collection MapFrom has no element TypeMap"
```

---

### Task 6: Runtime `IMapper.Map` test over explicit collection MapFrom

**Files:**
- Modify: `tests/MyAutoMapper.UnitTests/Runtime/MapperTests.cs`

- [ ] **Step 1: Append the failing test**

Add to the bottom of the file, inside the existing namespace:

```csharp
public class MapperExplicitCollectionMapFromTests
{
    private sealed class Src
    {
        public int Id { get; set; }
        public List<Src> Children { get; set; } = new();
    }

    private sealed class Dst
    {
        public int Id { get; set; }
        public List<Dst> Children { get; set; } = new();
    }

    private sealed class Profile : MappingProfile
    {
        public Profile()
        {
            CreateMap<Src, Dst>()
                .MaxDepth(2)
                .ForMember(d => d.Children, o => o.MapFrom(s => s.Children));
        }
    }

    [Fact]
    public void Map_runs_through_explicit_collection_MapFrom()
    {
        var cfg = new MapperConfiguration(new[] { new Profile() });
        var mapper = cfg.CreateMapper();

        var src = new Src
        {
            Id = 1,
            Children = { new Src { Id = 2, Children = { new Src { Id = 3 } } } }
        };

        var dst = mapper.Map<Dst>(src);

        dst.Id.Should().Be(1);
        dst.Children.Should().HaveCount(1);
        dst.Children[0].Id.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test tests/MyAutoMapper.UnitTests/MyAutoMapper.UnitTests.csproj --filter "FullyQualifiedName~MapperExplicitCollectionMapFromTests" --nologo -v q`
Expected: PASS. (Task 5 already made the underlying expression work.)

- [ ] **Step 3: Commit**

```bash
git add tests/MyAutoMapper.UnitTests/Runtime/MapperTests.cs
git commit -m "test(runtime): Mapper.Map over explicit collection MapFrom"
```

---

### Task 7: EF Core integration test

**Files:**
- Create: `tests/MyAutoMapper.IntegrationTests/EfCore/ExplicitCollectionMapFromTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;

namespace MyAutoMapper.IntegrationTests.EfCore;

public class ExplicitCollectionMapFromTests
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

    [Fact]
    public void Explicit_MapFrom_on_Children_projects_via_EF()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<Ctx>().UseSqlite(conn).Options;
        using var ctx = new Ctx(opts);
        ctx.Database.EnsureCreated();

        var root = new Category
        {
            Id = 1, Name = "root",
            Children =
            {
                new Category { Id = 2, Name = "L1",
                    Children = { new Category { Id = 3, Name = "L2" } } }
            }
        };
        ctx.Categories.Add(root);
        ctx.SaveChanges();

        var cfg = new MapperConfiguration(new[] { new Profile() });
        var proj = cfg.CreateProjectionProvider();

        var vm = ctx.Categories
            .Where(c => c.Id == 1)
            .ProjectTo<CategoryVm>(proj)
            .Single();

        vm.Name.Should().Be("root");
        vm.Children.Should().HaveCount(1);
        vm.Children[0].Name.Should().Be("L1");
        vm.Children[0].Children.Should().HaveCount(1);
        vm.Children[0].Children[0].Name.Should().Be("L2");
    }
}
```

- [ ] **Step 2: Run integration test**

Run: `dotnet test tests/MyAutoMapper.IntegrationTests/MyAutoMapper.IntegrationTests.csproj --filter "FullyQualifiedName~ExplicitCollectionMapFromTests" --nologo -v q`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/MyAutoMapper.IntegrationTests/EfCore/ExplicitCollectionMapFromTests.cs
git commit -m "test(efcore): integration test for explicit collection MapFrom"
```

---

### Task 8: Update the WebApiSample profile

**Files:**
- Modify: `samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs`

- [ ] **Step 1: Replace the comment with an explicit `ForMember`**

Final file content:

```csharp
using MyAutoMapper.WebApiSample.Entities;
using MyAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.WebApiSample.Profiles;

public class CategoryViewModelProfile : MappingProfile
{
    public CategoryViewModelProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Category, CategoryViewModel>()
            .MaxDepth(5)
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu))
            .ForMember(d => d.Children, o => o.MapFrom(src => src.Children));
    }
}
```

- [ ] **Step 2: Build the sample**

Run: `dotnet build samples/MyAutoMapper.WebApiSample/MyAutoMapper.WebApiSample.csproj -v q --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs
git commit -m "sample(webapi): replace Children convention with explicit MapFrom"
```

---

### Task 9: Full build and test sweep

- [ ] **Step 1: Clean build**

Run: `dotnet build -v q --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: All tests**

Run: `dotnet test -v q --nologo`
Expected: all tests pass; no skipped tests in existing suites; new tests visible.

- [ ] **Step 3: Check git status**

Run: `git status`
Expected: clean working tree; commits from Tasks 1-8 present on the current branch.

- [ ] **Step 4: Summary commit (optional)**

If any whitespace/format drift appeared, run `dotnet format` (if configured) and commit as `chore: format`.

---

## Risks & Mitigations

- **Overload ambiguity.** If a user has `MapFrom(Expression<Func<TSource, TMember>>)` and writes `o.MapFrom(s => s.Same)` (identical types), the C# compiler selects the non-generic overload (exact match). Verify in Task 3 indirectly by running the full unit test suite — existing scalar `MapFrom` tests use the legacy overload.
- **Error-message brittleness.** The assertion in `Explicit_without_element_TypeMap_throws_with_clear_message` uses wildcard matching (`*Items*Inner*InnerVm*`) so cosmetic message changes do not break it, but keep member names in the message.
- **Convention regression.** Task 4's third test guards that convention-path silent skip is preserved. Run the whole UnitTests project in Task 5 Step 4 as an extra safeguard.

## Subagent Notes

- Every task is independently reviewable — the earlier task's commits are the "checkpoint" for the next.
- Subagents executing individual tasks should use:
  - `mcp__plugin_serena_serena__find_symbol` / `get_symbols_overview` for orientation.
  - `mcp__plugin_serena_serena__replace_symbol_body` for Task 1/2/5 (smallest safe edit).
  - `mcp__plugin_serena_serena__insert_after_symbol` for Task 4/6 test additions.
  - `Write` for new files (Task 3, Task 7).
  - `Bash` only for `git`, `dotnet build`, `dotnet test`.
- Do NOT bypass pre-commit hooks. If a hook fails, fix the underlying issue and create a new commit.
