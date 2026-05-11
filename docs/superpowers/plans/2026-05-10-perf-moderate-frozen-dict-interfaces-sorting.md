# Performance Moderate: FrozenDictionary + GetInterfaces Dedup + Property Sort Cache

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three targeted startup/lookup optimizations: (1) swap `ConcurrentDictionary<TypePair, TypeMap>` for `FrozenDictionary` to eliminate sync overhead on every `Map<>` call; (2) dedup `GetInterfaces()` double-call in `TryGetElementType`; (3) cache sorted properties in `FlatteningConvention` to avoid per-call LINQ `OrderByDescending`.

**Architecture:** All three changes are self-contained. No cross-file dependencies between them. Apply in any order. `FrozenDictionary` requires refactoring `MapperConfiguration` constructor to collect into a mutable `Dictionary` first and freeze at end.

**Tech Stack:** .NET 10, C#, `System.Collections.Frozen` (in-box since .NET 8), xUnit, BenchmarkDotNet

---

## Files

| Action | Path | Purpose |
|--------|------|---------|
| Modify | `src/SmAutoMapper/Compilation/MapperConfiguration.cs` | `_typeMaps` → `FrozenDictionary`, refactor ctor and `BuildAndRegisterTypeMap` |
| Modify | `src/SmAutoMapper/Compilation/CollectionProjectionBuilder.cs` | Cache `GetInterfaces()` result in local var |
| Modify | `src/SmAutoMapper/Compilation/Conventions/FlatteningConvention.cs` | Add static sorted-properties cache, remove per-call `OrderByDescending` |
| Modify | `tests/SmAutoMapper.Benchmarks/ConfigurationBenchmark.cs` | Add benchmark for `GetTypeMap` lookup to measure FrozenDictionary gain |

---

### Task 1: Baseline — run all tests before touching anything

- [ ] **Step 1: Run all tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS. Stop here if any fail.

---

### Task 2: `MapperConfiguration` — switch `_typeMaps` to `FrozenDictionary`

**Files:**
- Modify: `src/SmAutoMapper/Compilation/MapperConfiguration.cs`

**What changes:**
- Add `using System.Collections.Frozen;`
- Field `_typeMaps` type: `ConcurrentDictionary<TypePair, TypeMap>` → `FrozenDictionary<TypePair, TypeMap>`
- Constructor: build into local `Dictionary<TypePair, TypeMap>`, call `.ToFrozenDictionary()` at end
- `BuildAndRegisterTypeMap`: return `TypeMap` instead of writing to `_typeMaps` directly
- Remove now-unused `using System.Collections.Concurrent`

- [ ] **Step 1: Write failing test that asserts `GetTypeMap` still works after refactor**

> This test verifies correctness and will act as regression guard. Add to `tests/SmAutoMapper.UnitTests/Compilation/MapperConfigurationTests.cs`:

```csharp
[Fact]
public void GetTypeMap_ReturnsSameTypeMap_ForRegisteredPair()
{
    var config = new MappingConfigurationBuilder()
        .AddProfile<SimpleProfile>()
        .Build();

    var map1 = config.GetTypeMap<SourceA, DestA>();
    var map2 = config.GetTypeMap<SourceA, DestA>();

    Assert.Same(map1, map2); // frozen — always same instance
}

[Fact]
public void GetTypeMap_Throws_ForUnregisteredPair()
{
    var config = new MappingConfigurationBuilder()
        .AddProfile<SimpleProfile>()
        .Build();

    Assert.Throws<InvalidOperationException>(
        () => config.GetTypeMap<DestA, SourceA>()); // reverse not registered
}

private sealed class SimpleProfile : MappingProfile
{
    public SimpleProfile() => CreateMap<SourceA, DestA>();
}
private record SourceA(string Name);
private record DestA { public string Name { get; init; } = ""; }
```

- [ ] **Step 2: Run the new tests — they should PASS (proving current behavior)**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release --filter "GetTypeMap_"
```

Expected: PASS (baseline behavior confirmed).

- [ ] **Step 3: Modify `MapperConfiguration.cs`**

Replace the entire file with:

```csharp
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using SmAutoMapper.Configuration;
using SmAutoMapper.Internal;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.Compilation;

public sealed class MapperConfiguration
{
    private readonly FrozenDictionary<TypePair, TypeMap> _typeMaps;
    private readonly List<ITypeMapConfiguration> _typeMapConfigs = [];
    private readonly ProjectionCompiler _projectionCompiler = new();
    private readonly InMemoryCompiler _inMemoryCompiler = new();

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    internal MapperConfiguration(IReadOnlyList<MappingProfile> profiles)
    {
        // Phase 1: collect all ITypeMapConfiguration and build catalog
        var allConfigs = new List<ITypeMapConfiguration>();
        foreach (var profile in profiles)
        {
            foreach (var cfg in profile.TypeMaps)
            {
                allConfigs.Add(cfg);
                if (cfg.ReverseTypeMap is not null)
                    allConfigs.Add(cfg.ReverseTypeMap);
            }
        }

        var catalogDict = new Dictionary<TypePair, ITypeMapConfiguration>();
        foreach (var c in allConfigs)
            catalogDict[new TypePair(c.SourceType, c.DestinationType)] = c;
        IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog = catalogDict;

        // Phase 2: compile all type maps into mutable dict, then freeze
        var mutableMaps = new Dictionary<TypePair, TypeMap>();
        foreach (var cfg in allConfigs)
        {
            _typeMapConfigs.Add(cfg);
            var typeMap = BuildTypeMap(cfg, catalog);
            mutableMaps[new TypePair(cfg.SourceType, cfg.DestinationType)] = typeMap;
        }

        _typeMaps = mutableMaps.ToFrozenDictionary();
    }

    public TypeMap GetTypeMap<TSource, TDest>()
        => GetTypeMap(typeof(TSource), typeof(TDest));

    public TypeMap GetTypeMap(Type sourceType, Type destType)
    {
        var typePair = new TypePair(sourceType, destType);
        if (_typeMaps.TryGetValue(typePair, out var typeMap))
            return typeMap;

        throw new InvalidOperationException(
            $"No mapping configured for {sourceType.Name} -> {destType.Name}. " +
            "Ensure a MappingProfile with CreateMap<TSource, TDest>() has been registered.");
    }

    internal bool HasTypeMap(Type sourceType, Type destType)
        => _typeMaps.ContainsKey(new TypePair(sourceType, destType));

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public IMapper CreateMapper() => new Mapper(this);

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public IProjectionProvider CreateProjectionProvider() => new ProjectionProvider(this);

    internal IReadOnlyCollection<TypeMap> GetAllTypeMaps() => _typeMaps.Values;

    internal IReadOnlyCollection<ITypeMapConfiguration> GetAllTypeMapConfigurations() => _typeMapConfigs;

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    private TypeMap BuildTypeMap(
        ITypeMapConfiguration typeMapConfig,
        IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog)
    {
        var typePair = new TypePair(typeMapConfig.SourceType, typeMapConfig.DestinationType);

        var seenNames = new HashSet<string>();
        var usedParams = new List<IParameterSlot>();
        foreach (var pm in typeMapConfig.PropertyMaps)
        {
            if (pm.HasParameterizedSource && pm.ParameterSlot is not null
                && seenNames.Add(pm.ParameterSlot.Name))
                usedParams.Add(pm.ParameterSlot);
        }

        var compilationResult = _projectionCompiler.CompileProjection(
            typePair,
            typeMapConfig.PropertyMaps,
            typeMapConfig.CustomConstructor,
            catalog,
            compilationStack: null);

        var compiledDelegate = _inMemoryCompiler.CompileDelegate(
            typePair, compilationResult.Projection);

        return new TypeMap(
            typePair,
            typeMapConfig.PropertyMaps,
            typeMapConfig.CustomConstructor,
            usedParams,
            compilationResult.Projection,
            compiledDelegate,
            compilationResult.ClosureHolderType,
            compilationResult.DefaultClosureHolder,
            compilationResult.HolderPropertyMap,
            compilationResult.HolderTypeInfo,
            typeMapConfig.MaxDepth);
    }
}
```

Key changes vs original:
- `using System.Collections.Frozen` added, `using System.Collections.Concurrent` removed
- `_typeMaps` field: `ConcurrentDictionary<TypePair, TypeMap>` → `FrozenDictionary<TypePair, TypeMap>`
- `BuildAndRegisterTypeMap` renamed to `BuildTypeMap`, now returns `TypeMap` instead of assigning
- `GetAllTypeMaps()` returns `_typeMaps.Values` directly (no copy)
- Also includes the startup LINQ chain fix from the Info plan (LINQ → foreach in usedParams)

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
git add src/SmAutoMapper/Compilation/MapperConfiguration.cs
git add tests/SmAutoMapper.UnitTests/Compilation/MapperConfigurationTests.cs
git commit -m "perf: replace ConcurrentDictionary with FrozenDictionary for read-only type map lookup"
```

---

### Task 3: `CollectionProjectionBuilder` — eliminate double `GetInterfaces()` call

**Files:**
- Modify: `src/SmAutoMapper/Compilation/CollectionProjectionBuilder.cs`

- [ ] **Step 1: Identify the lines to change**

Lines 76–81 of `CollectionProjectionBuilder.cs` (the fallback branch of `TryGetElementType`):

```csharp
// BEFORE (two GetInterfaces() calls):
if (type.GetInterfaces().Any(i =>
        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
    return false;
var ienum = type.GetInterfaces()
    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
```

- [ ] **Step 2: Apply the fix — cache interfaces in local variable**

Replace those 5 lines with:

```csharp
var interfaces = type.GetInterfaces();
if (interfaces.Any(i =>
        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
    return false;
var ienum = interfaces
    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
```

Only one `GetInterfaces()` call now — returns single `Type[]` reused for both checks.

- [ ] **Step 3: Build**

```powershell
dotnet build src/SmAutoMapper/ -c Release
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 4: Run tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmAutoMapper/Compilation/CollectionProjectionBuilder.cs
git commit -m "perf: cache GetInterfaces() result to avoid double allocation in TryGetElementType"
```

---

### Task 4: `FlatteningConvention` — cache sorted property arrays per type

**Files:**
- Modify: `src/SmAutoMapper/Compilation/Conventions/FlatteningConvention.cs`

**Problem:** `TryFlatten` recursively explores property chains (up to depth 5). Each recursive call calls `GetProperties()` + LINQ `OrderByDescending` — a new allocation per call per type. At startup with many nested types, this adds up.

**Fix:** Cache `PropertyInfo[]` sorted by name length per type in a `ConcurrentDictionary<Type, PropertyInfo[]>`. After first build, every lookup hits the cache.

- [ ] **Step 1: Replace `FlatteningConvention.cs`**

Replace the entire file with:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using SmAutoMapper.Internal;

namespace SmAutoMapper.Compilation.Conventions;

internal sealed class FlatteningConvention : INameConvention
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _sortedPropertiesCache = new();

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    public bool TryGetSourceExpression(
        Type sourceType,
        PropertyInfo destProperty,
        ParameterExpression sourceParam,
        out Expression? sourceExpression)
    {
        sourceExpression = TryFlatten(sourceType, destProperty.Name, destProperty.PropertyType, sourceParam);
        return sourceExpression is not null;
    }

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    private static PropertyInfo[] GetSortedProperties(Type type)
        => _sortedPropertiesCache.GetOrAdd(type, static t =>
            [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                  .OrderByDescending(p => p.Name.Length)]);

    [RequiresDynamicCode(AotMessages.DynamicCode)]
    [RequiresUnreferencedCode(AotMessages.UnreferencedCode)]
    private static Expression? TryFlatten(
        Type currentType,
        string remainingName,
        Type destPropertyType,
        Expression currentExpression,
        int depth = 0)
    {
        if (string.IsNullOrEmpty(remainingName) || depth > 5)
            return null;

        // Sorted properties are cached per type — no LINQ allocation after first access
        var properties = GetSortedProperties(currentType);

        foreach (var property in properties)
        {
            if (!remainingName.StartsWith(property.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var propertyAccess = Expression.Property(currentExpression, property);
            var remaining = remainingName[property.Name.Length..];

            if (remaining.Length == 0)
            {
                if (destPropertyType.IsAssignableFrom(property.PropertyType))
                    return propertyAccess;
                continue;
            }

            if (!property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            {
                var deeper = TryFlatten(property.PropertyType, remaining, destPropertyType, propertyAccess, depth + 1);
                if (deeper is not null)
                {
                    var deeperExpr = deeper.Type != destPropertyType
                        ? Expression.Convert(deeper, destPropertyType)
                        : deeper;

                    return Expression.Condition(
                        Expression.Equal(propertyAccess, Expression.Constant(null, property.PropertyType)),
                        Expression.Default(destPropertyType),
                        deeperExpr);
                }
            }
        }

        return null;
    }
}
```

Key changes vs original:
- Added `using System.Collections.Concurrent;`
- Added `_sortedPropertiesCache` static field
- Added `GetSortedProperties(Type)` static helper — `GetOrAdd` with collection expression spread (`[.. ...]`) to materialize sorted array
- Replaced `currentType.GetProperties(...).OrderByDescending(...)` in loop with `GetSortedProperties(currentType)` — O(1) after first access per type

- [ ] **Step 2: Build**

```powershell
dotnet build src/SmAutoMapper/ -c Release
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Run tests — including flattening convention tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release --filter "Flattening|Convention"
```

Then run full suite:

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add src/SmAutoMapper/Compilation/Conventions/FlatteningConvention.cs
git commit -m "perf: cache sorted PropertyInfo[] per type in FlatteningConvention to avoid per-call LINQ alloc"
```

---

### Task 5: Add `GetTypeMap` lookup benchmark

**Files:**
- Modify: `tests/SmAutoMapper.Benchmarks/ConfigurationBenchmark.cs`

- [ ] **Step 1: Add benchmark for hot lookup path**

Open `tests/SmAutoMapper.Benchmarks/ConfigurationBenchmark.cs` and append:

```csharp
[MemoryDiagnoser]
public class TypeMapLookupBenchmark
{
    private MapperConfiguration _config = null!;

    [GlobalSetup]
    public void Setup()
    {
        _config = new MappingConfigurationBuilder()
            .AddProfile<BenchmarkProfile>()
            .Build();
    }

    [Benchmark]
    public TypeMap GetTypeMap_ByGeneric()
        => _config.GetTypeMap<BenchmarkSource, BenchmarkDest>();

    [Benchmark]
    public TypeMap GetTypeMap_ByType()
        => _config.GetTypeMap(typeof(BenchmarkSource), typeof(BenchmarkDest));
}
```

> Use the `BenchmarkSource`/`BenchmarkDest` and `BenchmarkProfile` types already defined in `Models.cs` or `ConfigurationBenchmark.cs`.

- [ ] **Step 2: Run benchmark**

```powershell
dotnet run --project tests/SmAutoMapper.Benchmarks -c Release -- --filter "*TypeMapLookup*"
```

Expected: `Allocated` = 0 B (struct `TypePair` is stack-allocated; `FrozenDictionary.TryGetValue` does no heap allocation).

- [ ] **Step 3: Commit**

```bash
git add tests/SmAutoMapper.Benchmarks/ConfigurationBenchmark.cs
git commit -m "bench: add TypeMapLookupBenchmark to validate FrozenDictionary lookup perf"
```

---

## Self-Review Checklist

- [x] `FrozenDictionary.TryGetValue` — same signature as `ConcurrentDictionary.TryGetValue`, no callers need changing
- [x] `FrozenDictionary.ContainsKey` — available, `HasTypeMap` call compiles
- [x] `GetAllTypeMaps()` return type is `IReadOnlyCollection<TypeMap>` — `FrozenDictionary.Values` is `IReadOnlyList<TypeMap>` which implements that interface ✅
- [x] `_sortedPropertiesCache` in FlatteningConvention is `static` — shared across all convention instances (correct — type structure doesn't change at runtime)
- [x] Task 2 inline also applies the startup LINQ chain fix (usedParams foreach) — no conflict with Info plan
- [x] `System.Collections.Frozen` namespace is in-box since .NET 8, no NuGet package needed for .NET 10
