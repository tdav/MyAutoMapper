# Performance Info: Startup LINQ Chain Cleanup

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace LINQ chains that run at startup (configuration build time) with `foreach` + local `HashSet`/`List` to eliminate intermediate object allocations. Three locations: `ProjectionCompiler.CompileProjection`, `ConfigurationValidator.ValidateTypeMap`, and `GetAllTypeMaps()` copy-on-call.

**Architecture:** All changes are mechanical rewrites — same logic, no new abstractions. Touch only startup/validation code paths, zero impact to runtime mapping or projection. If the Moderate plan was applied (FrozenDictionary), `GetAllTypeMaps()` is already fixed there — skip Task 4 in that case.

**Tech Stack:** .NET 10, C#, xUnit

> **Dependency note:** Task 4 (`GetAllTypeMaps` cache) duplicates the fix that's already in the Moderate plan's Task 2. If you applied the Moderate plan first, skip Task 4 here.

---

## Files

| Action | Path | Purpose |
|--------|------|---------|
| Modify | `src/SmAutoMapper/Compilation/ProjectionCompiler.cs` | Replace 2 LINQ chains with foreach in `CompileProjection` |
| Modify | `src/SmAutoMapper/Validation/ConfigurationValidator.cs` | Replace LINQ in `ValidateTypeMap` with foreach |
| Modify | `src/SmAutoMapper/Compilation/MapperConfiguration.cs` | Cache `GetAllTypeMaps()` result — skip if Moderate plan applied |

---

### Task 1: Baseline

- [ ] **Step 1: Run all tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS. Stop if any fail.

---

### Task 2: `ProjectionCompiler` — replace LINQ chains with `foreach`

**Files:**
- Modify: `src/SmAutoMapper/Compilation/ProjectionCompiler.cs`

**Problem:** `CompileProjection` runs at startup for every registered type map. Lines 47, 51–52, and 64–66 use three LINQ chains that each allocate intermediate enumerators and lists.

**Current code (lines 46–66 in `CompileProjection`):**

```csharp
// Collect explicitly mapped destination property names
var explicitlyMapped = new HashSet<string>(
    propertyMaps.Select(pm => pm.DestinationProperty.Name));

// Check if this mapping has parameterized properties
var parameterizedMaps = propertyMaps
    .Where(pm => pm.HasParameterizedSource && pm.ParameterSlot is not null)
    .ToList();

// ...

if (parameterizedMaps.Count > 0)
{
    var slots = parameterizedMaps
        .Select(pm => pm.ParameterSlot!)
        .DistinctBy(s => s.Name)
        .ToList();
```

- [ ] **Step 1: Write regression test — verify explicit + parameterized mapping still works**

Add to `tests/SmAutoMapper.UnitTests/Compilation/MapperConfigurationTests.cs`:

```csharp
[Fact]
public void CompileProjection_ExplicitAndParameterized_BothResolveCorrectly()
{
    var minScore = Param<int>("MinScore");
    var config = new MappingConfigurationBuilder()
        .AddProfile(new MixedProfile(minScore))
        .Build();

    var mapper = config.CreateMapper();
    var result = mapper.Map<SourceP, DestP>(new SourceP("Alice", 80));

    Assert.Equal("Alice", result.Name);
    Assert.Equal(80, result.Score);
}

private sealed class MixedProfile(IParameterSlot<int> minScore) : MappingProfile
{
    public MixedProfile() : this(Param<int>("MinScore")) { }
    public MixedProfile(IParameterSlot<int> slot) 
    {
        CreateMap<SourceP, DestP>()
            .ForMember(d => d.Score, o => o.MapFrom((src, p) => src.Score, slot));
    }
}

private record SourceP(string Name, int Score);
private record DestP { public string Name { get; init; } = ""; public int Score { get; init; } }
```

- [ ] **Step 2: Run the test — should PASS**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release --filter "CompileProjection_ExplicitAndParameterized"
```

Expected: PASS.

- [ ] **Step 3: Replace the two LINQ chains in `ProjectionCompiler.CompileProjection`**

Find the block starting at line ~46 (inside `CompileProjection`, before the `foreach (var propertyMap in propertyMaps)` loop) and replace:

```csharp
// Collect explicitly mapped destination property names
var explicitlyMapped = new HashSet<string>(
    propertyMaps.Select(pm => pm.DestinationProperty.Name));

// Check if this mapping has parameterized properties
var parameterizedMaps = propertyMaps
    .Where(pm => pm.HasParameterizedSource && pm.ParameterSlot is not null)
    .ToList();
```

With:

```csharp
// Collect explicitly mapped destination property names + parameterized maps in one pass
var explicitlyMapped = new HashSet<string>(propertyMaps.Count);
var parameterizedMaps = new List<PropertyMap>();
foreach (var pm in propertyMaps)
{
    explicitlyMapped.Add(pm.DestinationProperty.Name);
    if (pm.HasParameterizedSource && pm.ParameterSlot is not null)
        parameterizedMaps.Add(pm);
}
```

Then find the `slots` LINQ chain inside the `if (parameterizedMaps.Count > 0)` block:

```csharp
var slots = parameterizedMaps
    .Select(pm => pm.ParameterSlot!)
    .DistinctBy(s => s.Name)
    .ToList();
```

Replace with:

```csharp
var slotNames = new HashSet<string>();
var slots = new List<IParameterSlot>();
foreach (var pm in parameterizedMaps)
{
    var slot = pm.ParameterSlot!;
    if (slotNames.Add(slot.Name))
        slots.Add(slot);
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build src/SmAutoMapper/ -c Release
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 5: Run all tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmAutoMapper/Compilation/ProjectionCompiler.cs
git add tests/SmAutoMapper.UnitTests/Compilation/MapperConfigurationTests.cs
git commit -m "perf: replace startup LINQ chains in ProjectionCompiler with foreach to reduce startup allocs"
```

---

### Task 3: `ConfigurationValidator` — replace LINQ with `foreach` in `ValidateTypeMap`

**Files:**
- Modify: `src/SmAutoMapper/Validation/ConfigurationValidator.cs`

**Problem:** `ValidateTypeMap` is called at startup for every registered type map. Lines 63–65 and 93–95 use LINQ to build a `HashSet` and a filtered list.

**Current code:**

```csharp
// Line 63–65
var mappedProperties = new HashSet<string>(
    typeMap.PropertyMaps.Select(pm => pm.DestinationProperty.Name));

// Line 93–95
var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Where(p => p.CanWrite)
    .ToList();
```

- [ ] **Step 1: Write regression test — validator still catches type mismatch**

Add to `tests/SmAutoMapper.UnitTests/Validation/ConfigurationValidatorTests.cs`:

```csharp
[Fact]
public void Validate_InvalidTypeMap_ThrowsMappingValidationException()
{
    var config = new MappingConfigurationBuilder()
        .AddProfile<InvalidProfile>()
        .Build();

    var validator = new ConfigurationValidator();
    var ex = Assert.Throws<MappingValidationException>(
        () => validator.Validate(config.GetAllTypeMaps()));

    Assert.Contains("SourceBad", ex.Message);
}

private sealed class InvalidProfile : MappingProfile
{
    public InvalidProfile()
    {
        CreateMap<SourceBad, DestBad>()
            .ForMember(d => d.Count, o => o.MapFrom(src => src.Name)); // string → int, invalid
    }
}
private record SourceBad(string Name);
private record DestBad { public int Count { get; init; } }
```

> `ConfigurationValidator` is `internal` — add `InternalsVisibleTo` if not already present, or test via the public `MapperConfiguration.Validate()` overload if one exists.

- [ ] **Step 2: Run the test — should PASS**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release --filter "Validate_InvalidTypeMap"
```

Expected: PASS.

- [ ] **Step 3: Replace LINQ in `ValidateTypeMap`**

In `ConfigurationValidator.cs`, inside `ValidateTypeMap`, replace:

```csharp
var mappedProperties = new HashSet<string>(
    typeMap.PropertyMaps.Select(pm => pm.DestinationProperty.Name));
```

With:

```csharp
var mappedProperties = new HashSet<string>(typeMap.PropertyMaps.Count);
foreach (var pm in typeMap.PropertyMaps)
    mappedProperties.Add(pm.DestinationProperty.Name);
```

Then replace:

```csharp
var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Where(p => p.CanWrite)
    .ToList();

foreach (var destProp in destProperties)
```

With:

```csharp
foreach (var destProp in destType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    if (!destProp.CanWrite)
        continue;
```

> Remove the closing `}` of the original `foreach` and add an extra `}` to close the new `if` guard block. The rest of the loop body is unchanged.

- [ ] **Step 4: Build**

```powershell
dotnet build src/SmAutoMapper/ -c Release
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 5: Run all tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmAutoMapper/Validation/ConfigurationValidator.cs
git add tests/SmAutoMapper.UnitTests/Validation/ConfigurationValidatorTests.cs
git commit -m "perf: replace LINQ in ConfigurationValidator.ValidateTypeMap with foreach to reduce startup allocs"
```

---

### Task 4: `MapperConfiguration.GetAllTypeMaps()` — cache result to avoid repeated copy

> **Skip this task if the Moderate plan was already applied** — that plan changes `_typeMaps` to `FrozenDictionary` whose `.Values` is `IReadOnlyList<TypeMap>` (no copy). Task 4 only applies when `_typeMaps` is still `ConcurrentDictionary`.

**Files:**
- Modify: `src/SmAutoMapper/Compilation/MapperConfiguration.cs`

**Problem:** `GetAllTypeMaps()` calls `_typeMaps.Values.ToList()` — allocates a new `List<TypeMap>` on every call even though the dictionary is never mutated after construction.

**Current:**
```csharp
internal IReadOnlyCollection<TypeMap> GetAllTypeMaps() => _typeMaps.Values.ToList();
```

- [ ] **Step 1: Add a lazy cache field and update the method**

In `MapperConfiguration`, add a private field after the existing field declarations:

```csharp
private IReadOnlyCollection<TypeMap>? _allTypeMapsCache;
```

Then replace the method body:

```csharp
internal IReadOnlyCollection<TypeMap> GetAllTypeMaps()
    => _allTypeMapsCache ??= [.. _typeMaps.Values];
```

This materializes the list once (first call, always at startup during validation) and caches it. The `??=` assignment is not thread-safe, but `GetAllTypeMaps` is only called from validation which runs synchronously during startup — no concurrent access concern.

- [ ] **Step 2: Build**

```powershell
dotnet build src/SmAutoMapper/ -c Release
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Run all tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add src/SmAutoMapper/Compilation/MapperConfiguration.cs
git commit -m "perf: cache GetAllTypeMaps() result to avoid repeated List<TypeMap> allocation"
```

---

## Self-Review Checklist

- [x] All changes are in startup/validation paths — zero impact on `Map<>` or `GetProjection` runtime
- [x] `HashSet<string>(propertyMaps.Count)` pre-sizes capacity — avoids resizing on typical input
- [x] `foreach` over `IReadOnlyList<PropertyMap>` — no boxing (list is not an interface in the enumerator)
- [x] `ValidateTypeMap` loop structure: added `if (!destProp.CanWrite) continue;` guard replaces the `.Where(p => p.CanWrite)` filter — equivalent logic
- [x] Task 4 dependency on Moderate plan noted — skip condition clearly stated
- [x] No new types, no new files, no new abstractions — mechanical rewrites only
