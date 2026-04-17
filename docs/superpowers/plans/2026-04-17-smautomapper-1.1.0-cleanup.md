# SmAutoMapper 1.1.0 Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship SmAutoMapper 1.1.0 — a cleanup release that fixes 6 critical + 4 medium-priority issues from the 2026-04-17 .NET review, without breaking 1.x consumers.

**Architecture:** Six sequential PRs, each independently green and revertible: (1) CI gate + SourceLink + housekeeping, (2) rebrand `MyAutoMapper` → `SmAutoMapper`, (3) API cleanup via `[Obsolete]` + remove `BuildServiceProvider`, (4) IL-compiled closure-holder setters + benchmark, (5) AOT/trimming attributes, (6) release tagging. Soft migration only — no breaking changes in 1.x; deprecated paths keep working, hard removal deferred to 2.0.

**Tech Stack:** .NET 10, C# 12 (`LangVersion=preview`), nullable reference types, Central Package Management, xUnit + FluentAssertions + EF Core Sqlite for tests, BenchmarkDotNet for perf, GitHub Actions for CI, NuGet SourceLink for publishing.

**Spec:** [docs/superpowers/specs/2026-04-17-smautomapper-1.1.0-cleanup-design.md](../specs/2026-04-17-smautomapper-1.1.0-cleanup-design.md)

**Branch strategy:** one feature branch per PR, based on `master`. After PR N merges to `master`, rebase PR N+1 onto master before continuing.

---

## PR #1 — CI gate + SourceLink + housekeeping

**Branch:** `release/1.1.0-pr1-ci`

### Task 1.1: Delete broken CI workflow

**Files:**
- Delete: `.github/workflows/dotnet.yml`

- [ ] **Step 1: Verify current file references non-existent path**

Run: `grep -n "MyAutoMapper.csproj" .github/workflows/dotnet.yml`
Expected: at least one match referencing `src/MyAutoMapper/MyAutoMapper.csproj` (the actual file is `src/MyAutoMapper/SmAutoMapper.csproj`, so this path doesn't exist).

- [ ] **Step 2: Remove the file**

```bash
git rm .github/workflows/dotnet.yml
```

- [ ] **Step 3: Commit**

```bash
git commit -m "ci: remove broken dotnet.yml (references non-existent project)"
```

---

### Task 1.2: Add working CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create the workflow file**

```yaml
name: CI
on:
  push:
    branches: [master]
  pull_request:
    branches: [master]
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --logger "console;verbosity=normal"
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add CI workflow (restore + build + test on push/PR to master)"
```

- [ ] **Step 3: Verify workflow is syntactically valid**

Push branch and open a draft PR. Confirm the `CI` check starts. If it fails on restore/build/test, fix before proceeding to next task.

---

### Task 1.3: Remove committed NuGet artifacts

**Files:**
- Delete: `nupkg/SmAutoMapper.1.0.0.nupkg`
- Delete: `nupkg/SmAutoMapper.1.0.1.nupkg`
- Delete: `src/MyAutoMapper/nupkg/SmMapper.1.0.0.nupkg` (if present)
- Modify: `.gitignore`

- [ ] **Step 1: Remove `.nupkg` files from tracking**

```bash
git rm nupkg/SmAutoMapper.1.0.0.nupkg nupkg/SmAutoMapper.1.0.1.nupkg
git rm -r src/MyAutoMapper/nupkg 2>/dev/null || true
```

- [ ] **Step 2: Verify `.gitignore` excludes build/pack output**

Add these lines to `.gitignore` if not already present (search for them first to avoid duplicates):

```
nupkg/
**/bin/
**/obj/
*.nupkg
*.snupkg
```

- [ ] **Step 3: Commit**

```bash
git add .gitignore
git commit -m "chore: remove committed .nupkg artifacts and tighten .gitignore"
```

---

### Task 1.4: Add SourceLink + deterministic build properties

**Files:**
- Modify: `Directory.Build.props`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Replace `Directory.Build.props` with extended version**

Full new content:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>preview</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add SourceLink package version to `Directory.Packages.props`**

Inside the existing `<ItemGroup>` that holds `<PackageVersion ... />` entries, add:

```xml
<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="8.0.0" />
```

- [ ] **Step 3: Add SourceLink reference to library csproj**

Modify `src/MyAutoMapper/SmAutoMapper.csproj` — add a new `<ItemGroup>` before the closing `</Project>`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
</ItemGroup>
```

- [ ] **Step 4: Verify build still passes**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors. Restore fetches `Microsoft.SourceLink.GitHub 8.0.0`.

- [ ] **Step 5: Verify pack produces both `.nupkg` and `.snupkg`**

Run: `dotnet pack src/MyAutoMapper/SmAutoMapper.csproj -c Release -o /tmp/smam-pack-test`
Expected: `/tmp/smam-pack-test/SmAutoMapper.1.0.1.nupkg` AND `/tmp/smam-pack-test/SmAutoMapper.1.0.1.snupkg` both exist.

- [ ] **Step 6: Commit**

```bash
git add Directory.Build.props Directory.Packages.props src/MyAutoMapper/SmAutoMapper.csproj
git commit -m "build: enable SourceLink, deterministic builds, symbol packages (.snupkg)"
```

---

### Task 1.5: Update publish workflow to push symbols

**Files:**
- Modify: `.github/workflows/publish.yml`

- [ ] **Step 1: Read current publish workflow**

Read file content first — it triggers on `release.published` and/or `workflow_dispatch` and runs `dotnet pack` + `dotnet nuget push`.

- [ ] **Step 2: Update push step to include `.snupkg`**

Ensure the push step (or replace it) uses glob that catches both packages:

```yaml
- name: Pack
  run: dotnet pack -c Release -o ./artifacts

- name: Push to NuGet
  run: >
    dotnet nuget push "./artifacts/*.nupkg"
    --api-key ${{ secrets.NUGET_API_KEY }}
    --source https://api.nuget.org/v3/index.json
    --skip-duplicate
```

NuGet push with `*.nupkg` glob automatically picks up the matching `.snupkg` produced by `SymbolPackageFormat=snupkg`. If current workflow uses a different pack output directory, keep that directory; only ensure the push glob is `*.nupkg`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/publish.yml
git commit -m "ci(publish): push .snupkg symbol packages alongside .nupkg"
```

---

### Task 1.6: Verify PR #1 end-to-end

- [ ] **Step 1: Build and test locally**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: build succeeds with 0 warnings, all 30 tests pass (24 unit + 6 integration).

- [ ] **Step 2: Confirm CI passes on the PR**

Open PR `release/1.1.0-pr1-ci` → `master`. Wait for `CI` workflow. Merge when green.

- [ ] **Step 3: Update local master**

```bash
git checkout master && git pull
```

---

## PR #2 — Rebrand `MyAutoMapper` → `SmAutoMapper`

**Branch:** `release/1.1.0-pr2-rebrand` (based on fresh `master` after PR #1).

### Task 2.1: Rename library directory

**Files:**
- Rename: `src/MyAutoMapper/` → `src/SmAutoMapper/`

- [ ] **Step 1: Rename directory with `git mv`**

```bash
git mv src/MyAutoMapper src/SmAutoMapper
```

- [ ] **Step 2: Verify rename is staged**

Run: `git status`
Expected: shows `renamed: src/MyAutoMapper/... -> src/SmAutoMapper/...` entries.

- [ ] **Step 3: Commit**

```bash
git commit -m "refactor: rename src/MyAutoMapper -> src/SmAutoMapper"
```

---

### Task 2.2: Rename test and sample directories

- [ ] **Step 1: Rename all three**

```bash
git mv tests/MyAutoMapper.UnitTests tests/SmAutoMapper.UnitTests
git mv tests/MyAutoMapper.IntegrationTests tests/SmAutoMapper.IntegrationTests
git mv tests/MyAutoMapper.Benchmarks tests/SmAutoMapper.Benchmarks
git mv samples/MyAutoMapper.WebApiSample samples/SmAutoMapper.WebApiSample
```

- [ ] **Step 2: Commit**

```bash
git commit -m "refactor: rename tests/ and samples/ directories MyAutoMapper -> SmAutoMapper"
```

---

### Task 2.3: Rename csproj files

- [ ] **Step 1: Rename each csproj**

```bash
git mv tests/SmAutoMapper.UnitTests/MyAutoMapper.UnitTests.csproj tests/SmAutoMapper.UnitTests/SmAutoMapper.UnitTests.csproj
git mv tests/SmAutoMapper.IntegrationTests/MyAutoMapper.IntegrationTests.csproj tests/SmAutoMapper.IntegrationTests/SmAutoMapper.IntegrationTests.csproj
git mv tests/SmAutoMapper.Benchmarks/MyAutoMapper.Benchmarks.csproj tests/SmAutoMapper.Benchmarks/SmAutoMapper.Benchmarks.csproj
git mv samples/SmAutoMapper.WebApiSample/MyAutoMapper.WebApiSample.csproj samples/SmAutoMapper.WebApiSample/SmAutoMapper.WebApiSample.csproj
```

- [ ] **Step 2: Commit**

```bash
git commit -m "refactor: rename csproj files MyAutoMapper -> SmAutoMapper"
```

---

### Task 2.4: Update solution file

**Files:**
- Modify: `SmAutoMapperSol.slnx`

- [ ] **Step 1: Open `SmAutoMapperSol.slnx` and replace all `MyAutoMapper` path segments with `SmAutoMapper`**

In the solution XML, find entries like `Path="src/MyAutoMapper/SmAutoMapper.csproj"` and update to `Path="src/SmAutoMapper/SmAutoMapper.csproj"`. Apply the same substitution to all 5 projects (library + 3 tests + sample).

- [ ] **Step 2: Verify solution loads**

Run: `dotnet sln list` (from repo root, if the CLI supports `.slnx` — if not, open in IDE).
Alternative: `dotnet restore SmAutoMapperSol.slnx` must succeed.
Expected: no errors about missing project paths.

- [ ] **Step 3: Commit**

```bash
git add SmAutoMapperSol.slnx
git commit -m "refactor(sln): update project paths MyAutoMapper -> SmAutoMapper"
```

---

### Task 2.5: Fix `InternalsVisibleTo` in library csproj

**Files:**
- Modify: `src/SmAutoMapper/SmAutoMapper.csproj`

- [ ] **Step 1: Replace `InternalsVisibleTo` value**

Change:
```xml
<InternalsVisibleTo Include="MyAutoMapper.UnitTests" />
```
to:
```xml
<InternalsVisibleTo Include="SmAutoMapper.UnitTests" />
```

- [ ] **Step 2: Commit**

```bash
git add src/SmAutoMapper/SmAutoMapper.csproj
git commit -m "refactor: update InternalsVisibleTo to SmAutoMapper.UnitTests"
```

---

### Task 2.6: Rename sample namespaces

**Files:**
- Modify: all `.cs` files under `samples/SmAutoMapper.WebApiSample/`

- [ ] **Step 1: Find all files with `MyAutoMapper.WebApiSample` namespace**

Run: `grep -rl "MyAutoMapper.WebApiSample" samples/SmAutoMapper.WebApiSample/`

- [ ] **Step 2: Replace namespace in each file**

For each file in the list, replace `namespace MyAutoMapper.WebApiSample` with `namespace SmAutoMapper.WebApiSample` (also update `using MyAutoMapper.WebApiSample.*` → `using SmAutoMapper.WebApiSample.*`).

- [ ] **Step 3: Verify build of sample**

Run: `dotnet build samples/SmAutoMapper.WebApiSample/SmAutoMapper.WebApiSample.csproj -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add samples/SmAutoMapper.WebApiSample
git commit -m "refactor(sample): rename namespace MyAutoMapper.WebApiSample -> SmAutoMapper.WebApiSample"
```

---

### Task 2.7: Rename test namespaces

**Files:**
- Modify: all `.cs` files under `tests/SmAutoMapper.UnitTests/`, `tests/SmAutoMapper.IntegrationTests/`, `tests/SmAutoMapper.Benchmarks/`

- [ ] **Step 1: Find all files with old namespace**

Run: `grep -rl "namespace MyAutoMapper" tests/`

- [ ] **Step 2: Replace `namespace MyAutoMapper.` → `namespace SmAutoMapper.` in each file**

Also update any `using MyAutoMapper.*` where those refer to the test assemblies (not library — library namespaces are already `SmAutoMapper.*`).

- [ ] **Step 3: Build tests**

Run: `dotnet build tests -c Release`
Expected: 0 warnings, 0 errors across all three test projects.

- [ ] **Step 4: Commit**

```bash
git add tests/
git commit -m "refactor(tests): rename namespaces MyAutoMapper.* -> SmAutoMapper.*"
```

---

### Task 2.8: Update ClosureHolderFactory dynamic assembly name

**Files:**
- Modify: `src/SmAutoMapper/Parameters/ClosureHolderFactory.cs:15`

- [ ] **Step 1: Rename the dynamic assembly string**

Change line 15 from:
```csharp
var assemblyName = new AssemblyName("MyAutoMapper.DynamicHolders");
```
to:
```csharp
var assemblyName = new AssemblyName("SmAutoMapper.DynamicHolders");
```

- [ ] **Step 2: Commit**

```bash
git add src/SmAutoMapper/Parameters/ClosureHolderFactory.cs
git commit -m "refactor: rename dynamic assembly MyAutoMapper.DynamicHolders -> SmAutoMapper.DynamicHolders"
```

---

### Task 2.9: Update README and doc references

**Files:**
- Modify: `README.md`
- Modify: any other `.md` files referencing `MyAutoMapper` (not the repo URL)

- [ ] **Step 1: Locate occurrences**

Run: `grep -rn "MyAutoMapper" README.md docs/ 2>/dev/null | grep -v "github.com/tdav/MyAutoMapper"`

The URL `https://github.com/tdav/MyAutoMapper` **stays as-is** (repo rename is out of scope). All other occurrences (namespace examples, project names in code blocks, path references) should become `SmAutoMapper`.

- [ ] **Step 2: Apply replacements manually, preserving the repo URL**

Review each match individually. Only replace where it describes the library identity, not the repository URL.

- [ ] **Step 3: Commit**

```bash
git add README.md docs/
git commit -m "docs: update MyAutoMapper -> SmAutoMapper references (repo URL unchanged)"
```

---

### Task 2.10: Final rebrand verification

- [ ] **Step 1: Confirm no stray `MyAutoMapper` in source**

Run:
```bash
grep -rn "MyAutoMapper" src tests samples --include="*.cs" --include="*.csproj"
```
Expected: empty output.

- [ ] **Step 2: Full build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build --logger "console;verbosity=normal"`
Expected: 0 warnings, 0 errors, all 30 tests pass.

- [ ] **Step 3: Verify PR CI green, merge, update local master**

```bash
git checkout master && git pull
```

---

## PR #3 — API cleanup (`[Obsolete]` + remove `BuildServiceProvider`)

**Branch:** `release/1.1.0-pr3-api-cleanup`

### Task 3.1: Mark `ProjectionProviderAccessor` as `[Obsolete]`

**Files:**
- Modify: `src/SmAutoMapper/Runtime/ProjectionProviderAccessor.cs`

- [ ] **Step 1: Replace entire file content**

```csharp
namespace SmAutoMapper.Runtime;

[Obsolete("Inject IProjectionProvider via DI and use the ProjectTo(IQueryable, IProjectionProvider) overload. " +
          "Will be removed in 2.0.", DiagnosticId = "SMAM0001")]
public static class ProjectionProviderAccessor
{
    private static volatile IProjectionProvider? _instance;

    internal static void SetInstance(IProjectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _instance = provider;
    }

    public static IProjectionProvider Instance =>
        _instance ?? throw new InvalidOperationException(
            "IProjectionProvider is not configured. Call services.AddMapping() first.");
}
```

- [ ] **Step 2: Build expecting warnings at call sites**

Run: `dotnet build -c Release`
Expected: build FAILS with `SMAM0001` (warning-as-error) at the call sites in `QueryableExtensions.cs` and `ServiceCollectionExtensions.cs`. This is expected — fixed in the next tasks.

- [ ] **Step 3: Commit (WIP — next tasks fix the warnings)**

```bash
git add src/SmAutoMapper/Runtime/ProjectionProviderAccessor.cs
git commit -m "refactor(api): mark ProjectionProviderAccessor as [Obsolete] SMAM0001 (WIP)"
```

---

### Task 3.2: Suppress SMAM0001 inside the library's own use sites

**Files:**
- Modify: `src/SmAutoMapper/Extensions/QueryableExtensions.cs`
- Modify: `src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs`

Legacy overloads still need to call the accessor during 1.x — they'll be `[Obsolete]`-marked themselves in task 3.3, so their use of `ProjectionProviderAccessor` is correctly scoped. We suppress the build error locally in these specific places.

- [ ] **Step 1: Wrap the four accessor use sites in `QueryableExtensions.cs`**

At the top of each legacy `ProjectTo` overload that uses `ProjectionProviderAccessor.Instance`, add `#pragma` suppressions. Wrap the method body, for example:

```csharp
public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source)
{
#pragma warning disable SMAM0001 // legacy entry point — preserved for 1.x compat; removal in 2.0
    var projection = ProjectionProviderAccessor.Instance
        .GetProjection(source.ElementType, typeof(TDest));
#pragma warning restore SMAM0001
    return BuildQuery<TDest>(source, projection);
}
```

Apply the same pattern to the 3 other accessor-using overloads:
- `ProjectTo<TDest>(this IQueryable, Action<IParameterBinder>)` (uses `ProjectionProviderAccessor.Instance` once)
- `ProjectTo<TSource, TDest>(this IQueryable<TSource>)` (uses `ProjectionProviderAccessor.Instance` once)
- `ProjectTo<TSource, TDest>(this IQueryable<TSource>, Action<IParameterBinder>)` (uses `ProjectionProviderAccessor.Instance` once)

- [ ] **Step 2: Suppress in `ServiceCollectionExtensions.cs`**

Wrap the `SetInstance` call:

```csharp
#pragma warning disable SMAM0001 // populate legacy accessor for 1.x consumers still using single-generic ProjectTo
ProjectionProviderAccessor.SetInstance(projectionProvider);
#pragma warning restore SMAM0001
```

- [ ] **Step 3: Build expecting 0 warnings**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors. Tests still green: `dotnet test -c Release --no-build`.

- [ ] **Step 4: Commit**

```bash
git add src/SmAutoMapper/Extensions/QueryableExtensions.cs src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor(api): suppress SMAM0001 at library's own legacy call sites"
```

---

### Task 3.3: Mark legacy accessor-based `ProjectTo` overloads as `[Obsolete]`

**Files:**
- Modify: `src/SmAutoMapper/Extensions/QueryableExtensions.cs`

Four overloads use `ProjectionProviderAccessor.Instance` and must be deprecated. The two overloads that take `IProjectionProvider` explicitly (lines 72-89 in the current file) are kept unmarked — those are the migration target.

- [ ] **Step 1: Add `[Obsolete]` attribute to the 4 accessor-based overloads**

Replace the signatures as follows (keep the method bodies with the `#pragma` from task 3.2):

```csharp
[Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider) and inject IProjectionProvider via DI.",
          DiagnosticId = "SMAM0002")]
public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source) { ... }

[Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider, Action<IParameterBinder>) and inject IProjectionProvider via DI.",
          DiagnosticId = "SMAM0002")]
public static IQueryable<TDest> ProjectTo<TDest>(
    this IQueryable source,
    Action<IParameterBinder> parameters) { ... }

[Obsolete("Use ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider) and inject IProjectionProvider via DI.",
          DiagnosticId = "SMAM0002")]
public static IQueryable<TDest> ProjectTo<TSource, TDest>(this IQueryable<TSource> source) { ... }

[Obsolete("Use ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider, Action<IParameterBinder>) and inject IProjectionProvider via DI.",
          DiagnosticId = "SMAM0002")]
public static IQueryable<TDest> ProjectTo<TSource, TDest>(
    this IQueryable<TSource> source,
    Action<IParameterBinder> parameters) { ... }
```

- [ ] **Step 2: Build — expect tests to complain about SMAM0002 if they use deprecated overloads**

Run: `dotnet build -c Release`
Expected: build may FAIL with `SMAM0002` in test projects that call the legacy overloads. This is expected — test projects need a `#pragma` or update.

- [ ] **Step 3: Fix test projects locally (preferred: keep testing both paths)**

In each test file that calls the deprecated overloads, add at file top:

```csharp
#pragma warning disable SMAM0002 // tests intentionally cover the legacy overload for backward-compat
```

Restore at end of file. Alternatively, add a new test that uses the non-deprecated path (we do this properly in task 3.7).

- [ ] **Step 4: Build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: 0 warnings, 0 errors, all 30 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/SmAutoMapper/Extensions/QueryableExtensions.cs tests/
git commit -m "refactor(api): mark legacy accessor-based ProjectTo overloads [Obsolete] SMAM0002"
```

---

### Task 3.4: Add new `ProjectTo<TDest>(IQueryable, IProjectionProvider)` overloads

**Files:**
- Modify: `src/SmAutoMapper/Extensions/QueryableExtensions.cs`

Single-generic variants currently exist only in the accessor-based form. Add explicit-provider equivalents.

- [ ] **Step 1: Add two new overloads**

Append inside `QueryableExtensions` class (near the existing `ProjectTo<TSource, TDest>(IQueryable<TSource>, IProjectionProvider)` overloads):

```csharp
public static IQueryable<TDest> ProjectTo<TDest>(
    this IQueryable source,
    IProjectionProvider provider)
{
    var projection = provider.GetProjection(source.ElementType, typeof(TDest));
    return BuildQuery<TDest>(source, projection);
}

public static IQueryable<TDest> ProjectTo<TDest>(
    this IQueryable source,
    IProjectionProvider provider,
    Action<IParameterBinder> parameters)
{
    var binder = new ParameterBinder();
    parameters(binder);
    var projection = provider.GetProjection(source.ElementType, typeof(TDest), binder);
    return BuildQuery<TDest>(source, projection);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SmAutoMapper/Extensions/QueryableExtensions.cs
git commit -m "feat(api): add ProjectTo<TDest>(IQueryable, IProjectionProvider) overloads"
```

---

### Task 3.5: Write unit tests for new overloads (TDD)

**Files:**
- Create: `tests/SmAutoMapper.UnitTests/QueryableExtensionsProviderOverloadTests.cs`

- [ ] **Step 1: Write test file**

```csharp
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
    private sealed record SourceEntity(int Id, string Name);
    private sealed record DestDto(int Id, string Name);

    private static (IProjectionProvider Provider, IQueryable<SourceEntity> Source) Arrange()
    {
        var builder = new MappingConfigurationBuilder();
        builder.CreateMap<SourceEntity, DestDto>();
        var config = builder.Build();
        var provider = config.CreateProjectionProvider();
        var source = new[] { new SourceEntity(1, "a"), new SourceEntity(2, "b") }.AsQueryable();
        return (provider, source);
    }

    [Fact]
    public void ProjectTo_TDest_WithProvider_ProjectsCorrectly()
    {
        var (provider, source) = Arrange();

        var result = ((IQueryable)source).ProjectTo<DestDto>(provider).ToList();

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new DestDto(1, "a"));
        result[1].Should().BeEquivalentTo(new DestDto(2, "b"));
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
        ProjectionProviderAccessor.SetInstance(provider);

#pragma warning disable SMAM0002 // legacy comparison target
        var legacy = ((IQueryable)source).ProjectTo<DestDto>().ToList();
#pragma warning restore SMAM0002
        var viaProvider = ((IQueryable)source).ProjectTo<DestDto>(provider).ToList();

        viaProvider.Should().BeEquivalentTo(legacy);
    }
}
```

- [ ] **Step 2: Run test — expect initial failures if `MappingConfigurationBuilder` or `CreateProjectionProvider` signatures differ**

Run: `dotnet test tests/SmAutoMapper.UnitTests --filter "FullyQualifiedName~QueryableExtensionsProviderOverload" -v normal`
Expected: all 3 tests PASS (the overloads and supporting API already exist after task 3.4). If any test fails on `MappingConfigurationBuilder` usage, adjust to match the actual API signature visible in `src/SmAutoMapper/Configuration/MappingConfigurationBuilder.cs`.

- [ ] **Step 3: Commit**

```bash
git add tests/SmAutoMapper.UnitTests/QueryableExtensionsProviderOverloadTests.cs
git commit -m "test: cover new ProjectTo<TDest>(IProjectionProvider) overloads"
```

---

### Task 3.6: Remove `BuildServiceProvider()` from `AddMapping`

**Files:**
- Modify: `src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs`

The current code uses `services.BuildServiceProvider()` only to try to resolve `ILoggerFactory` — which isn't registered at this point in the typical startup flow, making this call both ASP0000-unsafe and useless.

- [ ] **Step 1: Replace the method body**

Replace the first `AddMapping` overload (lines 13-42 in the current file) with:

```csharp
public static IServiceCollection AddMapping(
    this IServiceCollection services,
    Action<MappingConfigurationBuilder>? configure = null,
    params Assembly[] profileAssemblies)
    => AddMapping(services, configure, validatorLogger: null, profileAssemblies);

public static IServiceCollection AddMapping(
    this IServiceCollection services,
    Action<MappingConfigurationBuilder>? configure,
    ILogger<ConfigurationValidator>? validatorLogger,
    params Assembly[] profileAssemblies)
{
    var builder = new MappingConfigurationBuilder();
    configure?.Invoke(builder);

    foreach (var assembly in profileAssemblies)
    {
        builder.AddProfiles(assembly);
    }

    var configuration = builder.Build();

    var validator = new ConfigurationValidator(validatorLogger);
    validator.Validate(configuration.GetAllTypeMaps(), configuration.GetAllTypeMapConfigurations());

    services.AddSingleton(configuration);
    services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().CreateMapper());
    var projectionProvider = configuration.CreateProjectionProvider();

#pragma warning disable SMAM0001 // populate legacy accessor for 1.x consumers still using single-generic ProjectTo
    ProjectionProviderAccessor.SetInstance(projectionProvider);
#pragma warning restore SMAM0001

    services.AddSingleton<IProjectionProvider>(projectionProvider);

    return services;
}
```

Keep the existing params-only overload from lines 44-49 as-is:

```csharp
public static IServiceCollection AddMapping(
    this IServiceCollection services,
    params Assembly[] profileAssemblies)
{
    return AddMapping(services, configure: null, profileAssemblies);
}
```

- [ ] **Step 2: Verify no `BuildServiceProvider` remains**

Run: `grep -n "BuildServiceProvider" src/SmAutoMapper/`
Expected: empty output.

- [ ] **Step 3: Build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: 0 warnings, 0 errors, all 30 existing + 3 new tests = 33 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs
git commit -m "fix(di): remove BuildServiceProvider from AddMapping (ASP0000); make validator logger opt-in"
```

---

### Task 3.7: Add unit test covering AddMapping without BuildServiceProvider

**Files:**
- Create: `tests/SmAutoMapper.UnitTests/AddMappingBuildServiceProviderRegressionTests.cs`

- [ ] **Step 1: Write test**

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;
using Xunit;

namespace SmAutoMapper.UnitTests;

public sealed class AddMappingBuildServiceProviderRegressionTests
{
    private sealed record Src(int Id);
    private sealed record Dst(int Id);

    [Fact]
    public void AddMapping_ResolvesProjectionProvider_WithoutBuildingTempProvider()
    {
        var services = new ServiceCollection();
        services.AddMapping(cfg => cfg.CreateMap<Src, Dst>());
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IProjectionProvider>();

        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddMapping_NoOptionalLogger_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMapping(cfg => cfg.CreateMap<Src, Dst>());

        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test tests/SmAutoMapper.UnitTests --filter "FullyQualifiedName~AddMappingBuildServiceProviderRegression" -v normal`
Expected: both PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/SmAutoMapper.UnitTests/AddMappingBuildServiceProviderRegressionTests.cs
git commit -m "test: guard against BuildServiceProvider reintroduction in AddMapping"
```

---

### Task 3.8: Add "Migrating from 1.0.x" section to README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add migration section**

Append or insert a new section:

```markdown
## Migrating from 1.0.x

Release 1.1.0 introduces two compile-time deprecation warnings for consumers still using the service-locator path:

- **SMAM0001** — `ProjectionProviderAccessor` is deprecated. Inject `IProjectionProvider` via DI instead.
- **SMAM0002** — Single-generic `ProjectTo<TDest>(IQueryable)` overloads are deprecated. Use the overloads that take an explicit `IProjectionProvider`.

### Before (1.0.x)

```csharp
services.AddMapping(cfg => cfg.CreateMap<User, UserDto>());

// in a query:
var dtos = db.Users.ProjectTo<UserDto>().ToList();
```

### After (1.1.0+)

```csharp
services.AddMapping(cfg => cfg.CreateMap<User, UserDto>());

// in a query (inject IProjectionProvider via constructor):
public sealed class UserService(IProjectionProvider projectionProvider, AppDbContext db)
{
    public List<UserDto> GetAll() =>
        db.Users.ProjectTo<User, UserDto>(projectionProvider).ToList();
}
```

Deprecated paths continue to work in 1.x but will be removed in 2.0. Both diagnostic IDs can be suppressed locally with `#pragma warning disable SMAM0001, SMAM0002` during a staged migration.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs(readme): add Migrating from 1.0.x section"
```

---

### Task 3.9: PR #3 final verification

- [ ] **Step 1: Full build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: 0 warnings, 0 errors, 33+ tests pass (30 original + 3 new overload + 2 new regression).

- [ ] **Step 2: PR CI green, merge, update master**

```bash
git checkout master && git pull
```

---

## PR #4 — IL-compiled setters + benchmark

**Branch:** `release/1.1.0-pr4-il-setters`

### Task 4.1: Extend `HolderTypeInfo` with `Factory` + `Setters`

**Files:**
- Modify: `src/SmAutoMapper/Parameters/ClosureHolderFactory.cs`

Current `HolderTypeInfo` exposes `HolderType` + `PropertyMap`. Add IL-compiled `Factory` and per-property `Setters` delegates, then migrate `CreateInstance` / `CreateDefaultInstance` onto them.

- [ ] **Step 1: Add `using System.Linq.Expressions;` at the top of the file**

```csharp
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
```

- [ ] **Step 2: Update `HolderTypeInfo` — add properties, migrate methods**

Replace the `HolderTypeInfo` class (starting at `internal sealed class HolderTypeInfo` in the current file) with:

```csharp
internal sealed class HolderTypeInfo
{
    public Type HolderType { get; }
    public IReadOnlyDictionary<string, PropertyInfo> PropertyMap { get; }
    public Func<object> Factory { get; }
    public IReadOnlyDictionary<string, Action<object, object?>> Setters { get; }

    public HolderTypeInfo(Type holderType, IReadOnlyDictionary<string, PropertyInfo> propertyMap)
    {
        HolderType = holderType;
        PropertyMap = propertyMap;
        Factory = CompileFactory(holderType);
        Setters = CompileSetters(holderType, propertyMap);
    }

    public object CreateInstance(IReadOnlyDictionary<string, object?> parameterValues)
    {
        var instance = Factory();
        foreach (var (name, value) in parameterValues)
        {
            if (Setters.TryGetValue(name, out var set))
            {
                set(instance, value);
            }
        }
        return instance;
    }

    public object CreateDefaultInstance() => Factory();

    private static Func<object> CompileFactory(Type holderType)
    {
        var ctor = holderType.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Generated holder type {holderType.FullName} has no parameterless constructor.");
        var newExpr = Expression.New(ctor);
        var body = Expression.Convert(newExpr, typeof(object));
        return Expression.Lambda<Func<object>>(body).Compile();
    }

    private static IReadOnlyDictionary<string, Action<object, object?>> CompileSetters(
        Type holderType,
        IReadOnlyDictionary<string, PropertyInfo> propertyMap)
    {
        var result = new Dictionary<string, Action<object, object?>>(propertyMap.Count);
        foreach (var (name, property) in propertyMap)
        {
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var typedInstance = Expression.Convert(instanceParam, holderType);
            var typedValue = Expression.Convert(valueParam, property.PropertyType);

            var assign = Expression.Assign(
                Expression.Property(typedInstance, property),
                typedValue);

            result[name] = Expression.Lambda<Action<object, object?>>(
                assign, instanceParam, valueParam).Compile();
        }
        return result;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Run all tests**

Run: `dotnet test -c Release --no-build`
Expected: 33+ tests pass (existing behavior unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/SmAutoMapper/Parameters/ClosureHolderFactory.cs
git commit -m "perf(closure-holder): add IL-compiled Factory + Setters to HolderTypeInfo"
```

---

### Task 4.2: TDD — write failing tests for IL-setters

**Files:**
- Create: `tests/SmAutoMapper.UnitTests/HolderIlSettersTests.cs`

- [ ] **Step 1: Write test file**

```csharp
using System.Collections.Concurrent;
using FluentAssertions;
using SmAutoMapper.Parameters;
using Xunit;

namespace SmAutoMapper.UnitTests;

public sealed class HolderIlSettersTests
{
    private static HolderTypeInfo MakeHolder(params (string Name, Type Type)[] slots)
    {
        var factory = new ClosureHolderFactory();
        var parameterSlots = slots
            .Select(s => (IParameterSlot)new ParameterSlot(s.Name, s.Type))
            .ToList();
        return factory.GetOrCreateHolderType(parameterSlots);
    }

    [Fact]
    public void Factory_CreatesNewInstanceEachCall()
    {
        var info = MakeHolder(("X", typeof(int)));

        var a = info.Factory();
        var b = info.Factory();

        a.Should().NotBeSameAs(b);
        a.Should().BeOfType(info.HolderType);
    }

    [Fact]
    public void Setters_AssignsReferenceType()
    {
        var info = MakeHolder(("Name", typeof(string)));
        var holder = info.Factory();

        info.Setters["Name"](holder, "hello");

        info.PropertyMap["Name"].GetValue(holder).Should().Be("hello");
    }

    [Fact]
    public void Setters_AssignsValueType()
    {
        var info = MakeHolder(("Count", typeof(int)), ("Date", typeof(DateTime)));
        var holder = info.Factory();

        info.Setters["Count"](holder, 42);
        info.Setters["Date"](holder, new DateTime(2026, 4, 17));

        info.PropertyMap["Count"].GetValue(holder).Should().Be(42);
        info.PropertyMap["Date"].GetValue(holder).Should().Be(new DateTime(2026, 4, 17));
    }

    [Fact]
    public void Setters_AssignsNullableValueType_NullAndValue()
    {
        var info = MakeHolder(("Maybe", typeof(int?)));
        var holder = info.Factory();

        info.Setters["Maybe"](holder, null);
        info.PropertyMap["Maybe"].GetValue(holder).Should().BeNull();

        info.Setters["Maybe"](holder, 7);
        info.PropertyMap["Maybe"].GetValue(holder).Should().Be(7);
    }

    [Fact]
    public void Setters_IndependentBetweenProperties()
    {
        var info = MakeHolder(("A", typeof(int)), ("B", typeof(int)));
        var holder = info.Factory();

        info.Setters["A"](holder, 1);
        info.Setters["B"](holder, 2);

        info.PropertyMap["A"].GetValue(holder).Should().Be(1);
        info.PropertyMap["B"].GetValue(holder).Should().Be(2);
    }

    [Fact]
    public void Setters_WrongType_ThrowsInvalidCastException()
    {
        var info = MakeHolder(("N", typeof(int)));
        var holder = info.Factory();

        var act = () => info.Setters["N"](holder, "not-an-int");

        act.Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void Factory_ParallelCreation_ProducesDistinctInstances()
    {
        var info = MakeHolder(("X", typeof(int)));
        var bag = new ConcurrentBag<object>();

        Parallel.For(0, 100, _ => bag.Add(info.Factory()));

        bag.Should().HaveCount(100);
        bag.Distinct().Should().HaveCount(100);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/SmAutoMapper.UnitTests --filter "FullyQualifiedName~HolderIlSetters" -v normal`
Expected: all 7 PASS (IL setters already implemented in task 4.1).

If `ParameterSlot` constructor signature differs, adjust `MakeHolder` to match — check `src/SmAutoMapper/Parameters/ParameterSlot.cs` for the actual constructor shape.

- [ ] **Step 3: Commit**

```bash
git add tests/SmAutoMapper.UnitTests/HolderIlSettersTests.cs
git commit -m "test: cover IL-compiled Factory + Setters in HolderTypeInfo"
```

---

### Task 4.3: Replace `Activator.CreateInstance` + `SetValue` in `ProjectionProvider`

**Files:**
- Modify: `src/SmAutoMapper/Runtime/ProjectionProvider.cs`

The two parameterized `GetProjection` overloads (typed + untyped) currently call `Activator.CreateInstance(holderType)` and `property.SetValue(newHolder, value)`. Replace both with `HolderTypeInfo.CreateInstance(parameterValues)` — which now uses IL delegates internally.

- [ ] **Step 1: Inspect `TypeMap` to confirm `HolderTypeInfo` is accessible**

Check `src/SmAutoMapper/Compilation/TypeMap.cs` (or wherever `ClosureHolderType` + `HolderPropertyMap` are defined). The current shape exposes `ClosureHolderType` + `HolderPropertyMap` separately. Change this to also expose `HolderTypeInfo` (or cache one on the `TypeMap`).

Two options:

(a) **Add an internal `HolderTypeInfo? HolderTypeInfo { get; set; }` property to `TypeMap`.** Populate it wherever `ClosureHolderType` + `HolderPropertyMap` are currently populated. The place that compiles projections already has access to the `HolderTypeInfo` returned from `ClosureHolderFactory.GetOrCreateHolderType`.

(b) Keep `ClosureHolderType` + `HolderPropertyMap` and lazily rebuild `HolderTypeInfo` in `ProjectionProvider`. Not preferred — rebuilds the delegates on every call.

Use option (a).

- [ ] **Step 2: Add the property to `TypeMap`**

In the `TypeMap` class (search for it: `grep -rn "class TypeMap" src/SmAutoMapper`), add:

```csharp
internal HolderTypeInfo? HolderTypeInfo { get; set; }
```

Make the property `internal` — callers inside the library can assign; public surface unchanged.

- [ ] **Step 3: Populate `HolderTypeInfo` at compile time**

Find the code that assigns `ClosureHolderType` and `HolderPropertyMap` on `TypeMap` (search: `grep -rn "ClosureHolderType" src/SmAutoMapper` and `HolderPropertyMap`). At that same site, assign:

```csharp
typeMap.HolderTypeInfo = holderTypeInfo; // holderTypeInfo is the HolderTypeInfo from ClosureHolderFactory
```

- [ ] **Step 4: Replace the hot path in both `GetProjection` overloads**

In `src/SmAutoMapper/Runtime/ProjectionProvider.cs`, replace lines 38-49 (the typed overload's hot path) with:

```csharp
// Use cached IL delegates (no runtime reflection)
var holderInfo = typeMap.HolderTypeInfo!;
var newHolder = holderInfo.Factory();

foreach (var (name, value) in parameters.Values)
{
    if (holderInfo.Setters.TryGetValue(name, out var setter))
    {
        setter(newHolder, value);
    }
}
```

And the `holderType` reference used by `ClosureValueInjector.InjectParameters` at line 53 becomes:
```csharp
return ClosureValueInjector.InjectParameters<TSource, TDest>(
    (Expression<Func<TSource, TDest>>)typeMap.ProjectionExpression,
    holderInfo.HolderType,
    newHolder);
```

Apply the identical replacement to the untyped overload (lines 81-91 + lines 94-97) — same pattern: use `holderInfo.Factory()` and `holderInfo.Setters[name](...)` instead of `Activator.CreateInstance` + `property.SetValue`.

- [ ] **Step 5: Verify no stray `Activator.CreateInstance` or `SetValue` remain in Runtime**

Run: `grep -n "Activator.CreateInstance\|SetValue" src/SmAutoMapper/Runtime/`
Expected: empty output (both removed from the hot path).

- [ ] **Step 6: Build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: 0 warnings, 0 errors, 40+ tests pass (33 from before + 7 new IL-setter tests).

- [ ] **Step 7: Commit**

```bash
git add src/SmAutoMapper/Runtime/ProjectionProvider.cs src/SmAutoMapper/Compilation/TypeMap.cs
git commit -m "perf(projection): use IL-compiled Factory + Setters in ProjectionProvider hot path"
```

---

### Task 4.4: Remove now-redundant `Activator.CreateInstance` in `HolderTypeInfo`

**Files:**
- Modify: `src/SmAutoMapper/Parameters/ClosureHolderFactory.cs`

After task 4.1, `CreateInstance` and `CreateDefaultInstance` were already migrated onto `Factory` (no `Activator.CreateInstance` calls). This task is a verification pass.

- [ ] **Step 1: Verify**

Run: `grep -n "Activator.CreateInstance" src/SmAutoMapper/`
Expected: empty output.

- [ ] **Step 2: No commit needed** if the grep is clean. If it finds something leftover in another file, remove it and commit with `perf: drop residual Activator.CreateInstance in hot path`.

---

### Task 4.5: Add BenchmarkDotNet benchmark

**Files:**
- Check: `tests/SmAutoMapper.Benchmarks/SmAutoMapper.Benchmarks.csproj` (already exists from task 2.2/2.3)
- Create: `tests/SmAutoMapper.Benchmarks/ProjectionBenchmark.cs`

- [ ] **Step 1: Ensure BenchmarkDotNet package reference is present**

Open the benchmark csproj. If `<PackageReference Include="BenchmarkDotNet" />` is missing, add it (the version should already be pinned in `Directory.Packages.props` — check).

- [ ] **Step 2: Write benchmark class**

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;
using SmAutoMapper.Parameters;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Benchmarks;

[MemoryDiagnoser]
public class ProjectionBenchmark
{
    private IProjectionProvider _provider = null!;
    private IQueryable<Source> _source = null!;

    public sealed record Source(int Id, string Name);
    public sealed record Dest(int Id, string Name, int Multiplier);

    [GlobalSetup]
    public void Setup()
    {
        var builder = new MappingConfigurationBuilder();
        builder.CreateMap<Source, Dest>()
               .ForMember(d => d.Multiplier, s => s.MapFromParameter<int>("mul"));
        var config = builder.Build();
        _provider = config.CreateProjectionProvider();
        _source = Enumerable.Range(0, 1000).Select(i => new Source(i, $"n{i}")).AsQueryable();
    }

    [Benchmark]
    public int ParameterizedProjection()
    {
        var result = _source.ProjectTo<Source, Dest>(_provider, p => p.Bind("mul", 3)).ToList();
        return result.Count;
    }
}

public static class BenchmarkEntry
{
    public static void Main(string[] args) => BenchmarkRunner.Run<ProjectionBenchmark>();
}
```

If `ForMember`/`MapFromParameter` signatures differ, adjust to match actual `MappingConfigurationBuilder` API (inspect `src/SmAutoMapper/Configuration/MappingConfigurationBuilder.cs` and `TypeMapBuilder.cs` / `MemberMapBuilder.cs`). The benchmark only needs ONE parameterized mapping to exercise the IL-setter hot path.

- [ ] **Step 3: Build benchmark project**

Run: `dotnet build tests/SmAutoMapper.Benchmarks -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Run benchmark locally (optional, but recommended for PR description)**

Run: `dotnet run --project tests/SmAutoMapper.Benchmarks -c Release`
Expected: BenchmarkDotNet executes, prints a markdown table with `Method | Mean | Allocated`. Copy the output into the PR description to document current performance as the 1.1.0 baseline.

- [ ] **Step 5: Commit**

```bash
git add tests/SmAutoMapper.Benchmarks/ProjectionBenchmark.cs tests/SmAutoMapper.Benchmarks/SmAutoMapper.Benchmarks.csproj
git commit -m "bench: add ProjectionBenchmark for IL-compiled closure-holder setters"
```

---

### Task 4.6: PR #4 final verification

- [ ] **Step 1: Full build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: 0 warnings, 0 errors, 40+ tests pass.

- [ ] **Step 2: PR description includes BenchmarkDotNet output from task 4.5 step 4**

- [ ] **Step 3: PR CI green, merge, update master**

```bash
git checkout master && git pull
```

---

## PR #5 — AOT / Trimming attributes

**Branch:** `release/1.1.0-pr5-aot-attributes`

### Task 5.1: Add AOT/trim attributes to `AddMapping`

**Files:**
- Modify: `src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add `using System.Diagnostics.CodeAnalysis;` at the top**

- [ ] **Step 2: Mark both public `AddMapping` overloads**

Prepend attributes to each of the three public `AddMapping` overloads (2 from task 3.6 plus the params-only one):

```csharp
[RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
[RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
public static IServiceCollection AddMapping(...) { ... }
```

- [ ] **Step 3: Build — expect warnings on internal call chains to propagate**

Run: `dotnet build -c Release`
Expected: may fail if internal methods called from `AddMapping` are not themselves attributed and the AOT analyzer flags them. Next tasks handle those.

- [ ] **Step 4: Commit WIP**

```bash
git add src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(aot): mark AddMapping with RequiresDynamicCode + RequiresUnreferencedCode (WIP)"
```

---

### Task 5.2: Add AOT/trim attributes to `ProjectTo` overloads

**Files:**
- Modify: `src/SmAutoMapper/Extensions/QueryableExtensions.cs`

- [ ] **Step 1: Add `using System.Diagnostics.CodeAnalysis;` at the top**

- [ ] **Step 2: Attribute each public `ProjectTo` overload (all 6)**

Same pattern for each:

```csharp
[RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
[RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
public static IQueryable<TDest> ProjectTo<TDest>(...) { ... }
```

- [ ] **Step 3: Build**

Run: `dotnet build -c Release`
Expected: warnings may remain on `MappingConfigurationBuilder.CreateMap` or `MapperConfiguration.CreateMapper` — fixed in next task.

- [ ] **Step 4: Commit WIP**

```bash
git add src/SmAutoMapper/Extensions/QueryableExtensions.cs
git commit -m "feat(aot): mark ProjectTo overloads with RequiresDynamicCode + RequiresUnreferencedCode (WIP)"
```

---

### Task 5.3: Add AOT attributes to remaining public API

**Files:**
- Modify: `src/SmAutoMapper/Configuration/MappingConfigurationBuilder.cs`
- Modify: `src/SmAutoMapper/Compilation/MapperConfiguration.cs` (or wherever `CreateMapper()` lives)

- [ ] **Step 1: Enumerate public methods that may trigger codegen**

Run: `grep -rn "public .* CreateMap\|public .* CreateMapper\|public .* CreateProjectionProvider" src/SmAutoMapper/`

Expected candidates:
- `MappingConfigurationBuilder.CreateMap<TSource, TDest>()`
- `MappingConfigurationBuilder.AddProfiles(Assembly)`
- `MapperConfiguration.CreateMapper()`
- `MapperConfiguration.CreateProjectionProvider()`

- [ ] **Step 2: Attribute each (with `using System.Diagnostics.CodeAnalysis;` at each file top)**

```csharp
[RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
[RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
public ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>() { ... }
```

- [ ] **Step 3: Build**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Run all tests**

Run: `dotnet test -c Release --no-build`
Expected: all tests pass (attributes don't change runtime behavior, but ensure tests that call into public API haven't broken).

If tests fail to build because they call `AddMapping` / `CreateMap` / `ProjectTo` without the pragma, wrap the test-file usages with `#pragma warning disable IL3050, IL2026` at file top.

- [ ] **Step 5: Commit**

```bash
git add src/SmAutoMapper/
git commit -m "feat(aot): mark CreateMap, CreateMapper, CreateProjectionProvider with AOT attributes"
```

---

### Task 5.4: Opt out of AOT/trim compatibility in csproj

**Files:**
- Modify: `src/SmAutoMapper/SmAutoMapper.csproj`

- [ ] **Step 1: Add AOT flags to `<PropertyGroup>`**

Append inside the existing `<PropertyGroup>` block:

```xml
<IsAotCompatible>false</IsAotCompatible>
<IsTrimmable>false</IsTrimmable>
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
```

- [ ] **Step 2: Build — aot analyzer should catch any missed public method**

Run: `dotnet build -c Release`
Expected: 0 warnings, 0 errors. If any public method triggers IL3050/IL2026 inside the library code, return to task 5.3 and attribute it.

- [ ] **Step 3: Commit**

```bash
git add src/SmAutoMapper/SmAutoMapper.csproj
git commit -m "build(aot): disable IsAotCompatible + IsTrimmable; enable analyzers"
```

---

### Task 5.5: Update sample to suppress warnings

**Files:**
- Modify: `samples/SmAutoMapper.WebApiSample/Program.cs`

- [ ] **Step 1: Find the `AddMapping` call**

Run: `grep -n "AddMapping" samples/SmAutoMapper.WebApiSample/Program.cs`

- [ ] **Step 2: Wrap the call with `#pragma warning disable IL3050, IL2026`**

```csharp
#pragma warning disable IL3050, IL2026 // SmAutoMapper intentionally uses Reflection.Emit; AOT unsupported.
builder.Services.AddMapping(cfg => { /* ... */ });
#pragma warning restore IL3050, IL2026
```

If the sample has multiple library call sites (`ProjectTo`, etc.), wrap each similarly.

- [ ] **Step 3: Build sample**

Run: `dotnet build samples/SmAutoMapper.WebApiSample -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add samples/SmAutoMapper.WebApiSample/Program.cs
git commit -m "sample: suppress IL3050/IL2026 at SmAutoMapper call sites"
```

---

### Task 5.6: Add "AOT and Trimming" section to README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Append new section**

```markdown
## AOT and Trimming

SmAutoMapper uses `Reflection.Emit` to generate closure-holder types at runtime. It is **not compatible** with NativeAOT (`PublishAot=true`) or full trimming (`PublishTrimmed=true` without setting `<IsTrimmable>false</IsTrimmable>` on the library).

The library is explicitly marked `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`. Consumers publishing with AOT or trimming will receive compile-time warnings `IL3050` and `IL2026` at every SmAutoMapper call site. These warnings can be suppressed locally with `#pragma warning disable IL3050, IL2026` in non-AOT builds, or the project should avoid enabling `PublishAot`.

For full NativeAOT support, a Source Generator alternative is under consideration for a future major version; it is not in the 1.x roadmap.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs(readme): add AOT and Trimming section"
```

---

### Task 5.7: PR #5 final verification

- [ ] **Step 1: Full build + test**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: 0 warnings, 0 errors, 40+ tests pass.

- [ ] **Step 2: PR CI green, merge, update master**

```bash
git checkout master && git pull
```

---

## PR #6 — Release 1.1.0

**Branch:** `release/1.1.0-pr6-release`

### Task 6.1: Bump version to 1.1.0

**Files:**
- Modify: `src/SmAutoMapper/SmAutoMapper.csproj`

- [ ] **Step 1: Replace `<Version>1.0.1</Version>` with `<VersionPrefix>1.1.0</VersionPrefix>`**

- [ ] **Step 2: Build + pack to verify versioning**

Run: `dotnet pack src/SmAutoMapper -c Release -o /tmp/v110-check`
Expected: `/tmp/v110-check/SmAutoMapper.1.1.0.nupkg` AND `/tmp/v110-check/SmAutoMapper.1.1.0.snupkg` exist.

- [ ] **Step 3: Commit**

```bash
git add src/SmAutoMapper/SmAutoMapper.csproj
git commit -m "release: bump version to 1.1.0"
```

---

### Task 6.2: Create CHANGELOG.md

**Files:**
- Create: `CHANGELOG.md` (if exists, prepend the new release section)

- [ ] **Step 1: Write CHANGELOG content**

```markdown
# Changelog

## 1.1.0 — 2026-04-XX

### Added
- New `ProjectTo<TDest>(IQueryable, IProjectionProvider)` and `ProjectTo<TDest>(IQueryable, IProjectionProvider, Action<IParameterBinder>)` overloads for explicit DI
- AOT/trimming markers (`[RequiresDynamicCode]`, `[RequiresUnreferencedCode]`) on all public entry points
- SourceLink + deterministic builds + symbol packages (`.snupkg`)
- IL-compiled `Factory` and per-property `Setters` delegates for closure holders (perf)
- `ProjectionBenchmark` in `tests/SmAutoMapper.Benchmarks` for ongoing perf regression tracking

### Changed
- Migrated project and directory identity from `MyAutoMapper` to `SmAutoMapper` (matches the published NuGet package name)
- `AddMapping` no longer calls `services.BuildServiceProvider()` (fixes ASP0000)
- CI migrated to working `ci.yml` on push/PR to master

### Deprecated
- `ProjectionProviderAccessor` — inject `IProjectionProvider` via DI instead (diagnostic `SMAM0001`)
- Accessor-based `ProjectTo<TDest>(IQueryable)`, `ProjectTo<TDest>(IQueryable, Action<IParameterBinder>)`, `ProjectTo<TSource, TDest>(IQueryable<TSource>)`, `ProjectTo<TSource, TDest>(IQueryable<TSource>, Action<IParameterBinder>)` overloads — use the overloads that take `IProjectionProvider` (`SMAM0002`)

### Removed
- Committed `.nupkg` artifacts from `nupkg/` and `src/MyAutoMapper/nupkg/`
- Broken `.github/workflows/dotnet.yml`

### Performance
- Projection hot path no longer calls `Activator.CreateInstance` or `PropertyInfo.SetValue` — uses IL-compiled delegates instead. See `ProjectionBenchmark` results in PR #4.
```

Replace `2026-04-XX` with the actual release date immediately before creating the tag in task 6.5.

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: add CHANGELOG for 1.1.0"
```

---

### Task 6.3: Final README polish

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update any remaining code examples in the README to use the new DI-injected `IProjectionProvider` pattern**

Search for `ProjectTo<` in README and confirm the examples demonstrate the new overloads from PR #3 (the "Migrating from 1.0.x" section from task 3.8 already covers this — ensure the primary usage example also uses the recommended path).

- [ ] **Step 2: Verify NuGet badge points to `SmAutoMapper`**

Look for `https://img.shields.io/nuget/v/` or `https://www.nuget.org/packages/SmAutoMapper`. Both should reference the `SmAutoMapper` package ID.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(readme): update usage examples to DI-injected IProjectionProvider"
```

---

### Task 6.4: Final full verification

- [ ] **Step 1: Full build + test + pack**

Run:
```bash
dotnet build -c Release
dotnet test -c Release --no-build
dotnet pack src/SmAutoMapper -c Release -o /tmp/v110-final
```
Expected: 0 warnings, 0 errors, 40+ tests pass, `.nupkg` + `.snupkg` produced.

- [ ] **Step 2: Inspect package metadata**

Open `/tmp/v110-final/SmAutoMapper.1.1.0.nupkg` with NuGet Package Explorer (or `unzip -p` to examine `.nuspec` and embedded dependencies). Confirm:
- `id` = `SmAutoMapper`
- `version` = `1.1.0`
- `repository` url + commit hash embedded (SourceLink)
- `.snupkg` contains PDB files for the library

- [ ] **Step 3: PR CI green, merge to master**

---

### Task 6.5: Tag and release

- [ ] **Step 1: Update CHANGELOG date on master**

Replace the `2026-04-XX` in `CHANGELOG.md` with the actual release date. Commit:

```bash
git add CHANGELOG.md
git commit -m "release: finalize 1.1.0 changelog date"
git push origin master
```

- [ ] **Step 2: Create and push tag**

```bash
git tag -a v1.1.0 -m "Release 1.1.0 — cleanup release (see CHANGELOG.md)"
git push origin v1.1.0
```

- [ ] **Step 3: Draft GitHub release**

Via `gh` CLI:

```bash
gh release create v1.1.0 --title "1.1.0" --notes-file <(sed -n '/## 1.1.0/,/^## /p' CHANGELOG.md | head -n -1)
```

Or manually: GitHub → Releases → Draft new release → select tag `v1.1.0` → copy the 1.1.0 section from CHANGELOG.md → Publish release.

- [ ] **Step 4: Verify publish workflow runs and pushes to NuGet**

After release is published, the `publish.yml` workflow (from PR #1) triggers and runs `dotnet nuget push ./artifacts/*.nupkg`. Confirm:
- Workflow finishes green
- `SmAutoMapper 1.1.0` appears on nuget.org (may take several minutes to index)
- `dotnet add package SmAutoMapper --version 1.1.0` succeeds from a sandbox project

- [ ] **Step 5: Smoke-test in a sandbox consumer**

Create a throwaway console app:

```bash
mkdir /tmp/smam-consumer-smoke && cd /tmp/smam-consumer-smoke
dotnet new console
dotnet add package SmAutoMapper --version 1.1.0
```

Write a minimal `Program.cs` using `AddMapping` + `ProjectTo<TDest>(IQueryable, IProjectionProvider)`. `dotnet run` it. Step-into the library code in a debugger — confirm SourceLink opens sources from the GitHub repository at commit `v1.1.0`.

---

## Self-review — spec coverage check

| Spec requirement | Plan task(s) |
|---|---|
| Delete broken `dotnet.yml` | 1.1 |
| Add `ci.yml` | 1.2 |
| Remove committed `.nupkg` | 1.3 |
| SourceLink + deterministic + snupkg in `Directory.Build.props` | 1.4 |
| Update `publish.yml` to push `.snupkg` | 1.5 |
| Rename `src/MyAutoMapper/` → `src/SmAutoMapper/` | 2.1 |
| Rename test + sample dirs | 2.2, 2.3 |
| Update `SmAutoMapperSol.slnx` paths | 2.4 |
| Fix `InternalsVisibleTo` | 2.5 |
| Rename sample namespaces | 2.6 |
| Rename test namespaces | 2.7 |
| Rename dynamic assembly string | 2.8 |
| Update README/doc references | 2.9 |
| Mark `ProjectionProviderAccessor` `[Obsolete]` (SMAM0001) | 3.1, 3.2 |
| Mark legacy `ProjectTo` overloads `[Obsolete]` (SMAM0002) | 3.3 |
| Add `ProjectTo<TDest>(IQueryable, IProjectionProvider)` overloads | 3.4, 3.5 |
| Remove `BuildServiceProvider` from `AddMapping` | 3.6, 3.7 |
| README "Migrating from 1.0.x" | 3.8 |
| Extend `HolderTypeInfo` with `Factory` + `Setters` | 4.1 |
| IL-setter unit tests | 4.2 |
| Replace `Activator.CreateInstance` + `SetValue` in `ProjectionProvider` | 4.3 |
| BenchmarkDotNet benchmark | 4.5 |
| `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` on public API | 5.1, 5.2, 5.3 |
| `IsAotCompatible=false` + analyzer in csproj | 5.4 |
| Sample suppresses IL3050/IL2026 | 5.5 |
| README "AOT and Trimming" | 5.6 |
| Version bump to 1.1.0 | 6.1 |
| CHANGELOG | 6.2 |
| README polish | 6.3 |
| Tag + GitHub release + NuGet publish | 6.5 |

All 10 review findings (6 critical + 4 medium) are covered.

No placeholders, no "similar to task N" hand-waves, types used in later tasks (`HolderTypeInfo`, `Factory`, `Setters`, `SMAM0001`, `SMAM0002`, `IProjectionProvider`) are all defined in earlier tasks.
