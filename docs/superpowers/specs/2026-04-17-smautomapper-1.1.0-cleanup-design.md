# SmAutoMapper 1.1.0 — Cleanup Release Design

**Date:** 2026-04-17
**Status:** Approved for implementation
**Target version:** 1.1.0 (SemVer minor, additive + deprecations only)
**Scope:** Fix 6 critical issues + 4 medium-priority issues identified in the 2026-04-17 .NET expert review.

---

## 1. Overview

### 1.1. Goals

1. Unblock CI (currently broken: `.github/workflows/dotnet.yml` references a non-existent project path).
2. Unify project identity under the published NuGet package name `SmAutoMapper` (the codebase currently mixes `MyAutoMapper` file/namespace paths with the `SmAutoMapper` published package).
3. Remove Service Locator and ASP0000 anti-patterns without breaking 1.x consumers.
4. Make the library honest about its AOT/trimming incompatibility.
5. Improve projection hot-path performance by eliminating reflective `Activator.CreateInstance` and `PropertyInfo.SetValue` in favor of IL-compiled delegates.
6. Ship professional NuGet package hygiene: SourceLink, deterministic builds, symbol packages.

### 1.2. Non-goals (out of scope for 1.1.0)

- Source Generator alternative for AOT compatibility (scope of a future major).
- Removing the global static `HolderTypeInfo` cache (requires API rework; deferred to 2.0).
- `CancellationToken` propagation through `ProjectTo` (EF Core terminal methods already accept it; not a blocker).
- Testcontainers for integration tests (current SQLite in-memory coverage is sufficient).
- Minimal API migration for the WebApi sample.
- Renaming the GitHub repository (`github.com/tdav/MyAutoMapper` URL stays).

### 1.3. Strategy

**Soft migration via `[Obsolete]`** — no breaking changes in 1.x. Deprecated API continues to work; consumers get diagnostic IDs (`SMAM0001`, `SMAM0002`) with migration pointers. Hard removal happens in 2.0.

Work is split across **6 sequential PRs** so each step is independently reviewable, revertible, and CI-gated.

### 1.4. Deliverable summary

| PR | Title | Risk |
|----|-------|------|
| #1 | CI gate + SourceLink + housekeeping | Low |
| #2 | Rebrand `MyAutoMapper` → `SmAutoMapper` | Medium |
| #3 | API cleanup (Obsolete + remove `BuildServiceProvider`) | Low |
| #4 | Performance: IL-compiled setters + benchmark | Medium |
| #5 | AOT / trimming attributes | Low |
| #6 | Release 1.1.0 (CHANGELOG, version, tag, publish) | Low |

---

## 2. PR #1 — CI gate + SourceLink + housekeeping

### 2.1. Problems addressed

- Broken CI: `.github/workflows/dotnet.yml` references `src/MyAutoMapper/MyAutoMapper.csproj` which does not exist (actual csproj is `SmAutoMapper.csproj`).
- `.nupkg` artifacts committed to the repo (`nupkg/SmAutoMapper.1.0.0.nupkg`, `nupkg/SmAutoMapper.1.0.1.nupkg`, `src/MyAutoMapper/nupkg/SmMapper.1.0.0.nupkg`) — should not be in source control.
- No SourceLink, no deterministic build, no symbol package (`.snupkg`) — poor debugging experience for consumers.

### 2.2. Changes

**Delete** `.github/workflows/dotnet.yml` (broken).

**Create** `.github/workflows/ci.yml`:

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

**Update** `.github/workflows/publish.yml` to also push `.snupkg` (add `-SymbolPackageFormat snupkg` and include `**/*.snupkg` in push step).

**Update** `Directory.Build.props` — add SourceLink + deterministic build:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>preview</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <!-- NEW -->
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

**Update** `Directory.Packages.props` — add `<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="8.0.0" />`.

**Update** `src/MyAutoMapper/SmAutoMapper.csproj` — add `<PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />`.

**Update** `.gitignore` — add `nupkg/` and `**/bin/` safety entries if missing.

**`git rm`** the three committed `.nupkg` files.

### 2.3. Acceptance criteria

- `dotnet build -c Release` succeeds with 0 warnings, 0 errors.
- `dotnet test -c Release` runs and all 30 tests pass (24 unit + 6 integration).
- Push to `master` or PR triggers `CI` workflow and it passes green.
- `dotnet pack -c Release` produces both `.nupkg` and `.snupkg`.
- Package Explorer shows `repository url` + commit hash embedded via SourceLink.
- `git log --stat` shows `.nupkg` files removed.

### 2.4. Risk: Low

Workflow and metadata changes only; no source-level semantics change.

---

## 3. PR #2 — Rebrand `MyAutoMapper` → `SmAutoMapper`

### 3.1. Problems addressed

- Project directory `src/MyAutoMapper/`, test directory `tests/MyAutoMapper.*`, sample directory `samples/MyAutoMapper.WebApiSample/` all use the legacy `MyAutoMapper` name, while the published NuGet package and root library namespaces use `SmAutoMapper`. Confusing for consumers and contributors.
- `InternalsVisibleTo` still references `MyAutoMapper.UnitTests`.
- Sample namespace `MyAutoMapper.WebApiSample.*` is inconsistent.

### 3.2. Changes

**Directory renames:**

- `src/MyAutoMapper/` → `src/SmAutoMapper/`
- `tests/MyAutoMapper.UnitTests/` → `tests/SmAutoMapper.UnitTests/`
- `tests/MyAutoMapper.IntegrationTests/` → `tests/SmAutoMapper.IntegrationTests/`
- `samples/MyAutoMapper.WebApiSample/` → `samples/SmAutoMapper.WebApiSample/`

**File renames inside:**

- Test project files: `MyAutoMapper.UnitTests.csproj` → `SmAutoMapper.UnitTests.csproj` (same for others).
- Sample csproj: `MyAutoMapper.WebApiSample.csproj` → `SmAutoMapper.WebApiSample.csproj`.

**Update** `SmAutoMapperSol.slnx` — update all 5 project paths.

**Namespace updates** in samples + tests:

- `namespace MyAutoMapper.WebApiSample.*` → `namespace SmAutoMapper.WebApiSample.*` (library namespaces are already `SmAutoMapper.*`).
- Test project namespaces aligned to `SmAutoMapper.UnitTests` / `SmAutoMapper.IntegrationTests`.

**Update** `InternalsVisibleTo` in library csproj:

```xml
<!-- was: <InternalsVisibleTo Include="MyAutoMapper.UnitTests" /> -->
<InternalsVisibleTo Include="SmAutoMapper.UnitTests" />
```

**Update** README badge URLs, example code blocks, installation snippets.

**Keep** `https://github.com/tdav/MyAutoMapper` repository URL — renaming the repo is out of scope.

### 3.3. Acceptance criteria

- `grep -r "MyAutoMapper" src tests samples --include="*.cs" --include="*.csproj"` returns empty.
- `dotnet build -c Release` passes (0 warnings).
- All 30 tests green.
- CI (from PR #1) is green on the PR.
- Solution opens in IDE without broken project references.

### 3.4. Risk: Medium

Many files touched. Mitigated by CI gate from PR #1 — build and tests break immediately if a path is missed.

---

## 4. PR #3 — API cleanup (soft-obsolete)

### 4.1. Problems addressed

- `ProjectionProviderAccessor` is a **Service Locator** anti-pattern: static mutable global state used by extension methods.
- Single-generic `ProjectTo<TDest>(this IQueryable)` overloads rely on this accessor, forcing global mutable dependency.
- `AddMapping` calls `services.BuildServiceProvider()` during registration — classic **ASP0000** anti-pattern; used only to try to resolve `ILoggerFactory`, which typically isn't registered yet at that point.

### 4.2. Changes

**Mark `ProjectionProviderAccessor` as `[Obsolete]`** in `src/SmAutoMapper/Runtime/ProjectionProviderAccessor.cs`:

```csharp
[Obsolete("Inject IProjectionProvider via DI and use the ProjectTo(IQueryable, IProjectionProvider) overload. " +
          "Will be removed in 2.0.", DiagnosticId = "SMAM0001")]
public static class ProjectionProviderAccessor { ... }
```

`SetInstance` + `Instance` remain functional for 1.x compatibility; consumers get `SMAM0001` warnings.

**Add new `ProjectTo` overloads** in `src/SmAutoMapper/Extensions/QueryableExtensions.cs`:

```csharp
public static IQueryable<TDest> ProjectTo<TDest>(
    this IQueryable source,
    IProjectionProvider projectionProvider);

public static IQueryable<TDest> ProjectTo<TDest>(
    this IQueryable source,
    IProjectionProvider projectionProvider,
    Action<IParameterBinder> configure);
```

**Mark legacy single-generic overloads as `[Obsolete]`:**

```csharp
[Obsolete("Use ProjectTo<TDest>(IQueryable, IProjectionProvider) and inject the provider via DI.",
          DiagnosticId = "SMAM0002")]
public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source) { ... }
```

Two-generic `ProjectTo<TSource, TDest>` overloads are **not** deprecated (they're correct), but gain new variants that accept `IProjectionProvider` explicitly.

**Remove `BuildServiceProvider()` from `AddMapping`** in `src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs`:

```csharp
public static IServiceCollection AddMapping(
    this IServiceCollection services,
    Action<IMappingConfigurator> configure,
    ILogger<ConfigurationValidator>? validatorLogger = null)
{
    var validator = new ConfigurationValidator(validatorLogger);
    // ... rest of registration without BuildServiceProvider
}
```

**Library-internal code** migrates to the new API path during this PR (no `SMAM0001`/`SMAM0002` warnings inside the library itself).

**README** gains a "Migrating from 1.0.x" section showing old → new API mapping.

### 4.3. Acceptance criteria

- `dotnet build -c Release` — 0 warnings in library code (library uses new API internally).
- All 30 existing tests green.
- New unit test: `ProjectTo(IProjectionProvider)` produces same results as legacy overload for an identical query.
- Legacy consumer compiles with `SMAM0001` + `SMAM0002` warnings (verified with a sandbox project in PR review).
- README "Migrating from 1.0.x" section present.

### 4.4. Risk: Low

New API is additive; deprecated path still works. Warnings are the intended migration signal, not regressions.

---

## 5. PR #4 — Performance: IL-compiled setters + benchmark

### 5.1. Problems addressed

- `ProjectionProvider.GetProjection` uses `Activator.CreateInstance(holderType)` and `property.SetValue(holder, value)` per request, despite a comment claiming "no runtime reflection". The hot path allocates boxed reflection overhead on every query.

### 5.2. Changes

**Extend `HolderTypeInfo`** in `src/SmAutoMapper/Parameters/ClosureHolderFactory.cs`:

```csharp
internal sealed class HolderTypeInfo
{
    public Type HolderType { get; }
    public IReadOnlyDictionary<string, PropertyInfo> Properties { get; }

    // NEW:
    public Func<object> Factory { get; }
    public IReadOnlyDictionary<string, Action<object, object?>> Setters { get; }
}
```

Construction (one-time per holder type, cached):

- `Factory`: `Expression.Lambda<Func<object>>(Expression.New(ctor)).Compile()`.
- Each setter: `Expression.Lambda<Action<object, object?>>` with `Expression.Assign(Expression.Property(Convert(instance, holderType), prop), Convert(value, prop.PropertyType))`, compiled once.

Cached globally in the existing static `ConcurrentDictionary<string, HolderTypeInfo>` (global state deferred to 2.0).

**Rewrite `GetProjection`** in `src/SmAutoMapper/Runtime/ProjectionProvider.cs`:

```csharp
var holder = holderInfo.Factory();
foreach (var (name, value) in parameters)
    holderInfo.Setters[name](holder, value);
```

No `Activator.CreateInstance`, no `SetValue` in the hot path.

**New tests** in `tests/SmAutoMapper.UnitTests/HolderIlSettersTests.cs`:

- Reference type (string) — setter writes, getter reads.
- Value type (int, DateTime) — correct boxing/unboxing.
- Nullable value type (int?, DateTime?) — null and non-null.
- Multiple properties on a single holder — setters independent.
- Wrong-type assignment throws `InvalidCastException` (parity with `PropertyInfo.SetValue`).
- Parallel invocation of `Factory()` from 100 threads — type-safe, all instances distinct.

**New benchmark project** `tests/SmAutoMapper.Benchmarks` (BenchmarkDotNet already in `Directory.Packages.props`):

- `[MemoryDiagnoser]` on the projection benchmark.
- Baseline: legacy reflection path (kept behind `USE_REFLECTION_HOLDER` compile flag for this PR only).
- New: IL setters path.
- Flag removed before merge; before/after numbers recorded in PR description.

### 5.3. Acceptance criteria

- All 30 existing tests + new IL-setter tests green.
- `grep -n "Activator.CreateInstance" src/SmAutoMapper` returns empty.
- `grep -n "\.SetValue(" src/SmAutoMapper/Runtime` returns empty (outside one-time initialization, if any).
- PR description includes BenchmarkDotNet before/after tables showing reduced allocations and latency.

### 5.4. Risk: Medium

IL / Expression Tree code generation can produce `InvalidProgramException` on edge cases. Mitigated by the explicit test matrix above plus the benchmark doubling as an end-to-end smoke test.

---

## 6. PR #5 — AOT / Trimming attributes

### 6.1. Problems addressed

- Library uses `Reflection.Emit` (`ModuleBuilder`, `TypeBuilder.DefineField/Method/Property`) in `ClosureHolderFactory`. This is fundamentally incompatible with NativeAOT and full trimming, but the library currently ships no warnings — consumers learn this only at runtime.

### 6.2. Changes

**Add attributes** on all public entry points in:

- `src/SmAutoMapper/Extensions/ServiceCollectionExtensions.cs` — all `AddMapping` overloads.
- `src/SmAutoMapper/Extensions/QueryableExtensions.cs` — all `ProjectTo` overloads (including the new ones from PR #3).
- `src/SmAutoMapper/Configuration/MappingConfigurator.cs` — `IMappingConfigurator.CreateMap<,>`, `CreateMapper()`.

Pattern:

```csharp
[RequiresDynamicCode("SmAutoMapper uses Reflection.Emit to generate closure holder types at runtime.")]
[RequiresUnreferencedCode("SmAutoMapper uses reflection over mapped types; members may be trimmed.")]
public static IServiceCollection AddMapping(...) { ... }
```

**Update** `src/SmAutoMapper/SmAutoMapper.csproj`:

```xml
<PropertyGroup>
  <IsAotCompatible>false</IsAotCompatible>
  <IsTrimmable>false</IsTrimmable>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
</PropertyGroup>
```

`EnableAotAnalyzer=true` causes build failures if future public API is added without the attributes — compile-time guarantee of honesty.

**Update sample** `samples/SmAutoMapper.WebApiSample/Program.cs` — suppress warnings locally with comment:

```csharp
#pragma warning disable IL3050, IL2026 // SmAutoMapper intentionally uses Reflection.Emit; AOT unsupported.
builder.Services.AddMapping(cfg => ...);
#pragma warning restore IL3050, IL2026
```

Sample csproj does **not** enable `PublishAot`.

**Update README** — new section "AOT and Trimming":

> SmAutoMapper uses `Reflection.Emit` to generate closure-holder types at runtime. It is **not compatible** with NativeAOT (`PublishAot=true`) or full trimming. The library is marked `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`. Consumers get compile-time warnings `IL3050` / `IL2026` if they attempt AOT/trimmed publish. For full AOT support, a Source Generator alternative is under consideration for a future major version.

### 6.3. Acceptance criteria

- `dotnet build -c Release` — 0 warnings in library (all public API has attributes).
- Optional `tests/SmAutoMapper.AotGuardTests` project (CI-only) that references SmAutoMapper, calls `AddMapping` without `#pragma`, and expects `IL3050` / `IL2026` (verified via `dotnet build --warnaserror` in a separate CI step).
- All 30 existing tests green.
- README "AOT and Trimming" section present.

### 6.4. Risk: Low

Attributes are metadata; runtime behavior does not change. Main risk is forgetting an attribute on a new public method — mitigated by `EnableAotAnalyzer=true` at build time.

---

## 7. PR #6 — Release 1.1.0

### 7.1. Version bump

In `src/SmAutoMapper/SmAutoMapper.csproj`:

```xml
<!-- remove: <Version>1.0.1</Version> -->
<VersionPrefix>1.1.0</VersionPrefix>
```

Pre-release suffix (e.g., `preview.1`) added via CI `-p:VersionSuffix=preview.N` when desired.

### 7.2. CHANGELOG.md

Create at repository root:

```markdown
# Changelog

## 1.1.0 — 2026-04-XX

### Added
- New `ProjectTo<TDest>(IQueryable, IProjectionProvider)` overloads for explicit DI
- AOT/trimming markers (`[RequiresDynamicCode]`, `[RequiresUnreferencedCode]`)
- SourceLink + deterministic builds + symbol packages (.snupkg)
- IL-compiled factory and setter delegates for closure holders (perf)

### Changed
- Migrated from `MyAutoMapper` identity to unified `SmAutoMapper` (matches NuGet package)
- `AddMapping` no longer calls `BuildServiceProvider()` (fixes ASP0000)
- CI migrated to working `ci.yml` (previous `dotnet.yml` referenced non-existent paths)

### Deprecated
- `ProjectionProviderAccessor` — inject `IProjectionProvider` via DI instead (SMAM0001)
- Single-generic `ProjectTo<TDest>(IQueryable)` overloads — use overload with `IProjectionProvider` (SMAM0002)

### Removed
- Committed `.nupkg` artifacts from `nupkg/` and `src/MyAutoMapper/nupkg/`

### Performance
- Reduced allocations and latency on hot-path projection requests
  (BenchmarkDotNet results in PR #4)
```

### 7.3. README updates

- Add "Migrating from 1.0.x" section (links to deprecated API + new overloads, with code snippets).
- Add "AOT and Trimming" section (from PR #5).
- Update example code to use `IProjectionProvider` via DI.
- Verify NuGet badge (`SmAutoMapper`) still resolves correctly.

### 7.4. Release process

Manual (not automated in this PR):

1. PR #6 merges to `master`.
2. Tag: `git tag -a v1.1.0 -m "Release 1.1.0" && git push origin v1.1.0`.
3. GitHub → Releases → Draft new release → select tag → copy CHANGELOG section.
4. Publish → triggers `publish.yml` (fixed in PR #1) → package + symbols pushed to NuGet.

### 7.5. Acceptance criteria

- `dotnet pack -c Release` produces `SmAutoMapper.1.1.0.nupkg` + `SmAutoMapper.1.1.0.snupkg`.
- `nuget verify -Signatures` passes on the published package.
- Package Explorer shows: correct repository URL + commit hash (SourceLink); `.snupkg` contains PDB; `Deterministic=true`.
- `dotnet add package SmAutoMapper --version 1.1.0` in a sandbox project works; step-into in debugger opens sources from GitHub.
- CHANGELOG.md, README.md updated and committed.

### 7.6. Risk: Low

Risk concentrated in prior PRs (regressions, missed namespaces). Mitigated by final review of CHANGELOG against master commit log before tagging.

---

## 8. Known limitations and out-of-scope

### 8.1. Intentionally left as-is

- **Global static `HolderTypeInfo` cache** in `ClosureHolderFactory` — read-mostly, thread-safe via `ConcurrentDictionary`; removing requires API rework. Defer to 2.0.
- **`ProjectionProviderAccessor.SetInstance` still invoked from `AddMapping`** for 1.x compatibility. `[Obsolete]` marker is sufficient for SemVer honesty. Hard removal in 2.0.
- **No `CancellationToken` propagation in `ProjectTo`** — EF Core terminal methods accept tokens directly; not a practical blocker.
- **No Source Generator** — alternative AOT path comparable in scope to a new library; deferred.
- **GitHub repo URL unchanged** — `github.com/tdav/MyAutoMapper` stays; repo rename is manual migration out of code scope.

### 8.2. Consolidated risk table

| Source | Likelihood | Mitigation |
|---|---|---|
| Missed `MyAutoMapper` string after rebrand (PR #2) | Medium | `grep -r "MyAutoMapper"` in acceptance criteria |
| IL-setter throws `InvalidProgramException` on edge-case type (PR #4) | Medium | Test matrix covers reference/value/nullable/multi-prop/parallel |
| Consumer with `TreatWarningsAsErrors=true` breaks on new `SMAM0001`/`SMAM0002` (PR #3) | Low | Warnings, not errors; fix with `#pragma` or migrate |
| AOT analyzer false positives in library code (PR #5) | Low | All public API attributed; `EnableAotAnalyzer=true` catches misses at build |
| Downstream breaks through NuGet version range | Low | Changes are additive; SemVer minor is correct |

### 8.3. Consumer migration checklist (README)

1. Bump `SmAutoMapper` to `1.1.0`.
2. Replace `queryable.ProjectTo<Dto>()` with `queryable.ProjectTo<Dto>(projectionProvider)` (DI-injected).
3. Remove any use of `ProjectionProviderAccessor`.
4. If `TreatWarningsAsErrors=true` and `SMAM0001`/`SMAM0002` block the build — migrate immediately or suppress locally with `#pragma` while migrating.
5. If the project publishes AOT (`PublishAot=true`) — disable it; SmAutoMapper is incompatible with NativeAOT.

### 8.4. What consumers get in 1.1.0

- Green CI on every PR/push (currently broken).
- Consistent `SmAutoMapper` identity throughout (no more `MyAutoMapper`/`SmAutoMapper` confusion).
- SourceLink + symbols (step-into debugging).
- Honest AOT/trimming markers (no silent runtime surprise).
- Clean DI migration path away from Service Locator.
- Faster hot-path via IL-compiled setters.
- `AddMapping` free of ASP0000.
