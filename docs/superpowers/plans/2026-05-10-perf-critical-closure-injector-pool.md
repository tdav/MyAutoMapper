# Performance Critical: ClosureValueInjector Pooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate per-request heap allocation and O(n) expression-tree walk in `ClosureValueInjector` by reusing one instance per thread via `[ThreadStatic]`.

**Architecture:** `ClosureValueInjector` currently creates a new instance on every `GetProjection(IParameterBinder)` call (hot path in web apps). Replace the `readonly` constructor fields with mutable `Initialize`/`Reset` methods and a `[ThreadStatic]` cache so each thread reuses one instance. No new NuGet dependencies — pure BCL.

**Tech Stack:** .NET 10, C#, xUnit (unit tests), BenchmarkDotNet (micro-benchmark)

---

## Files

| Action | Path | Purpose |
|--------|------|---------|
| Modify | `src/SmAutoMapper/Compilation/ClosureValueInjector.cs` | Add ThreadStatic pool, Initialize/Reset, remove readonly ctor |
| Modify | `tests/SmAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs` | Add concurrent + multi-call correctness tests |
| Modify | `tests/SmAutoMapper.Benchmarks/ProjectionBenchmark.cs` | Add parameterized benchmark to measure improvement |

---

### Task 1: Verify baseline — existing tests pass before any change

**Files:**
- Read: `src/SmAutoMapper/Compilation/ClosureValueInjector.cs`
- Run: `tests/SmAutoMapper.UnitTests/`

- [ ] **Step 1: Run full unit test suite**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release --no-build
```

Expected: all tests PASS. If any fail — stop and fix before proceeding.

- [ ] **Step 2: Run existing benchmarks to record baseline**

```powershell
dotnet run --project tests/SmAutoMapper.Benchmarks -c Release -- --filter "*Projection*" --exporters json
```

Record the `GetProjection` numbers from the console output. Save them — you'll compare after the fix.

---

### Task 2: Add failing concurrent-correctness test

**Files:**
- Modify: `tests/SmAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs`

- [ ] **Step 1: Add the test**

Open `tests/SmAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs` and append:

```csharp
[Fact]
public async Task GetProjection_WithParameters_ReturnsDifferentExpressionsPerValue_Concurrent()
{
    // Arrange
    var config = new MappingConfigurationBuilder()
        .AddProfile<ParameterizedProfile>()
        .Build();
    var provider = config.CreateProjectionProvider();

    const int threadCount = 8;
    const int iterationsPerThread = 200;

    var results = new System.Collections.Concurrent.ConcurrentBag<(int Expected, int ActualConst)>();

    // Act — hammer GetProjection from many threads simultaneously
    await Task.WhenAll(Enumerable.Range(0, threadCount).Select(threadIdx => Task.Run(() =>
    {
        for (var i = 0; i < iterationsPerThread; i++)
        {
            var expectedValue = threadIdx * 1000 + i;
            var binder = new ParameterBinder();
            binder.Set("MinPrice", expectedValue);

            var expr = provider.GetProjection<SourceWithPrice, DestWithPrice>(binder);

            // Extract the constant value from the expression tree
            var visitor = new ConstantExtractor(typeof(int));
            visitor.Visit(expr);
            results.Add((expectedValue, visitor.Found));
        }
    })));

    // Assert — every thread got its own value, no cross-contamination
    foreach (var (expected, actual) in results)
        Assert.Equal(expected, actual);
}

// Helper: walks expression tree and extracts first constant of given type
private sealed class ConstantExtractor(Type targetType) : ExpressionVisitor
{
    public int Found { get; private set; }
    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == targetType)
            Found = (int)node.Value!;
        return base.VisitConstant(node);
    }
}

// Helper profile for parameterized projection
private sealed class ParameterizedProfile : MappingProfile
{
    public ParameterizedProfile()
    {
        var minPrice = Param<int>("MinPrice");
        CreateMap<SourceWithPrice, DestWithPrice>()
            .ForMember(d => d.FilteredPrice, o => o.MapFrom((src, p) => src.Price > p ? src.Price : 0, minPrice));
    }
}

private record SourceWithPrice(decimal Price);
private record DestWithPrice { public decimal FilteredPrice { get; init; } }
```

> **Note:** The test references `ParameterBinder` from `SmAutoMapper.Parameters` — add the using if not present: `using SmAutoMapper.Parameters;`

- [ ] **Step 2: Run just this new test — expect FAIL or PASS (it tests correctness, not the optimization)**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release --filter "GetProjection_WithParameters_ReturnsDifferentExpressionsPerValue_Concurrent" -v normal
```

Expected: PASS (the current code is correct, just slow). If FAIL — review the test helper types and fix the model classes.

---

### Task 3: Refactor `ClosureValueInjector` — add ThreadStatic pool

**Files:**
- Modify: `src/SmAutoMapper/Compilation/ClosureValueInjector.cs`

- [ ] **Step 1: Replace the file body**

Replace the entire content of `src/SmAutoMapper/Compilation/ClosureValueInjector.cs` with:

```csharp
using System.Linq.Expressions;

namespace SmAutoMapper.Compilation;

internal sealed class ClosureValueInjector : ExpressionVisitor
{
    [ThreadStatic]
    private static ClosureValueInjector? _instance;

    private Type? _holderType;
    private object? _newHolderInstance;

    private ClosureValueInjector() { }

    private void Initialize(Type holderType, object newHolderInstance)
    {
        _holderType = holderType;
        _newHolderInstance = newHolderInstance;
    }

    private void Reset()
    {
        _holderType = null;
        _newHolderInstance = null;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == _holderType)
            return Expression.Constant(_newHolderInstance, _holderType);
        return base.VisitConstant(node);
    }

    /// <summary>
    /// Swaps all closure holder instances in the expression tree with a new holder containing
    /// updated parameter values. Reuses a per-thread injector to avoid heap allocation per call.
    /// </summary>
    public static Expression<Func<TSource, TDest>> InjectParameters<TSource, TDest>(
        Expression<Func<TSource, TDest>> template,
        Type holderType,
        object newHolderInstance)
    {
        var injector = _instance ??= new ClosureValueInjector();
        injector.Initialize(holderType, newHolderInstance);
        try
        {
            return (Expression<Func<TSource, TDest>>)injector.Visit(template);
        }
        finally
        {
            injector.Reset();
        }
    }

    /// <summary>
    /// Non-generic version for when types are not known at compile time.
    /// </summary>
    public static LambdaExpression InjectParameters(
        LambdaExpression template,
        Type holderType,
        object newHolderInstance)
    {
        var injector = _instance ??= new ClosureValueInjector();
        injector.Initialize(holderType, newHolderInstance);
        try
        {
            return (LambdaExpression)injector.Visit(template);
        }
        finally
        {
            injector.Reset();
        }
    }
}
```

Key changes vs. original:
- Removed `private readonly` fields → now nullable mutable fields
- Removed ctor parameters → parameterless `private` ctor
- Added `[ThreadStatic] private static ClosureValueInjector? _instance`
- Added `Initialize(Type, object)` and `Reset()` methods
- Both static `InjectParameters` overloads now rent `_instance`, init, visit, then `Reset()` in `finally`
- Public API (method signatures) unchanged — callers in `ProjectionProvider` need no changes

- [ ] **Step 2: Build to verify no compile errors**

```powershell
dotnet build src/SmAutoMapper/ -c Release
```

Expected: Build succeeded, 0 error(s).

---

### Task 4: Run all tests — verify no regression

**Files:**
- Run: `tests/SmAutoMapper.UnitTests/`
- Run: `tests/SmAutoMapper.IntegrationTests/`

- [ ] **Step 1: Run unit tests**

```powershell
dotnet test tests/SmAutoMapper.UnitTests/ -c Release
```

Expected: all PASS including the new concurrent test from Task 2.

- [ ] **Step 2: Run integration tests**

```powershell
dotnet test tests/SmAutoMapper.IntegrationTests/ -c Release
```

Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add src/SmAutoMapper/Compilation/ClosureValueInjector.cs
git add tests/SmAutoMapper.UnitTests/Runtime/ProjectionProviderTests.cs
git commit -m "perf: pool ClosureValueInjector per thread to eliminate per-request heap alloc"
```

---

### Task 5: Add benchmark to measure improvement

**Files:**
- Modify: `tests/SmAutoMapper.Benchmarks/ProjectionBenchmark.cs`

- [ ] **Step 1: Add parameterized projection benchmark**

Open `tests/SmAutoMapper.Benchmarks/ProjectionBenchmark.cs` and append a new benchmark class:

```csharp
[MemoryDiagnoser]
public class ParameterizedProjectionBenchmark
{
    private IProjectionProvider _provider = null!;
    private ParameterBinder _binder = null!;

    [GlobalSetup]
    public void Setup()
    {
        var config = new MappingConfigurationBuilder()
            .AddProfile<BenchmarkParameterizedProfile>()
            .Build();
        _provider = config.CreateProjectionProvider();
        _binder = new ParameterBinder();
        _binder.Set("MinScore", 50);
    }

    [Benchmark]
    public object GetProjection_Parameterized()
        => _provider.GetProjection<BenchmarkSource, BenchmarkDest>(_binder);

    private sealed class BenchmarkParameterizedProfile : MappingProfile
    {
        public BenchmarkParameterizedProfile()
        {
            var minScore = Param<int>("MinScore");
            CreateMap<BenchmarkSource, BenchmarkDest>()
                .ForMember(d => d.Score, o => o.MapFrom((src, p) => src.Score > p ? src.Score : 0, minScore));
        }
    }
}
```

> Use the same `BenchmarkSource`/`BenchmarkDest` models that already exist in `tests/SmAutoMapper.Benchmarks/Models.cs`, or add them if missing.

- [ ] **Step 2: Run benchmark, compare with baseline from Task 1**

```powershell
dotnet run --project tests/SmAutoMapper.Benchmarks -c Release -- --filter "*ParameterizedProjection*"
```

Expected: `Allocated` column shows **0 B** per call (was: 1 heap object per call). Mean time should also improve due to eliminated GC pressure.

- [ ] **Step 3: Commit**

```bash
git add tests/SmAutoMapper.Benchmarks/ProjectionBenchmark.cs
git commit -m "bench: add ParameterizedProjectionBenchmark to track injector pool perf"
```

---

## Self-Review Checklist

- [x] All 3 public API call sites (`ProjectionProvider` lines 43, 76) — no changes needed, callers unchanged
- [x] Thread safety: `[ThreadStatic]` gives each thread its own instance; `Visit` is not re-entrant (VisitConstant doesn't call back into Visit)
- [x] `Reset()` is in `finally` — no state leak on exception
- [x] Both generic and non-generic overloads updated
- [x] No new NuGet dependency added
- [x] Concurrent test covers the cross-thread contamination scenario
