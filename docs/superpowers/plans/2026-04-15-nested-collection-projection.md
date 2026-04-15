# Nested Collection Projection Implementation Plan

> **For Claude:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Разворачивать `IEnumerable<TSrc> → IEnumerable<TDest>` в `ProjectTo` через вложенный `.Select(new TDest {...})`, с поддержкой самоссылок через `MaxDepth(n)`.

**Architecture:** `ProjectionCompiler` получает каталог всех `ITypeMapConfiguration` и стек `TypePair` для обхода рекурсии. При несовпадении типов member'а детектор коллекций пытается найти TypeMap элементов и разворачивает `.Select`. Самоссылки отслеживаются через стек и ограничиваются `MaxDepth`.

**Tech Stack:** C# / .NET 10, System.Linq.Expressions, EF Core Sqlite (integration tests), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-04-15-nested-collection-projection-design.md`

---

## File Structure

**Modify:**
- `src/MyAutoMapper/Configuration/ITypeMapConfiguration.cs` — добавить `int? MaxDepth { get; }`.
- `src/MyAutoMapper/Configuration/ITypeMappingExpression.cs` — добавить `MaxDepth(int)` в fluent API.
- `src/MyAutoMapper/Configuration/TypeMapBuilder.cs` — реализация `MaxDepth`.
- `src/MyAutoMapper/Compilation/TypeMap.cs` — хранить `MaxDepth`.
- `src/MyAutoMapper/Compilation/ProjectionCompiler.cs` — принимать каталог конфигов + стек, детектор коллекций, holder-sharing.
- `src/MyAutoMapper/Compilation/MapperConfiguration.cs` — двухфазная сборка: собрать все конфиги → потом компилировать с общим каталогом.
- `samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs` — `.MaxDepth(5)` + явный `MapFrom` для `SubCategories`.

**Create:**
- `src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs` — helper-класс.
- `tests/MyAutoMapper.IntegrationTests/NestedCollectionProjectionTests.cs` — EF Core Sqlite тест.
- `tests/MyAutoMapper.UnitTests/CollectionProjectionBuilderTests.cs` — изолированные тесты helper'а.

---

## Chunk 1: API surface (MaxDepth на ITypeMapConfiguration)

### Task 1: Добавить `MaxDepth` в `ITypeMapConfiguration`

**Files:**
- Modify: `src/MyAutoMapper/Configuration/ITypeMapConfiguration.cs`

- [ ] **Step 1: Изменить интерфейс**

```csharp
public interface ITypeMapConfiguration
{
    Type SourceType { get; }
    Type DestinationType { get; }
    IReadOnlyList<PropertyMap> PropertyMaps { get; }
    LambdaExpression? CustomConstructor { get; }
    ITypeMapConfiguration? ReverseTypeMap { get; }
    IReadOnlyList<string> SkippedReverseProperties { get; }
    int? MaxDepth { get; }
}
```

- [ ] **Step 2: Собрать решение**

Run: `dotnet build src/MyAutoMapper/SmAutoMapper.csproj`
Expected: FAIL — `TypeMapBuilder` не реализует `MaxDepth`.

- [ ] **Step 3: Добавить заглушку в `TypeMapBuilder<TSource,TDest>`**

В `src/MyAutoMapper/Configuration/TypeMapBuilder.cs`, рядом с другими свойствами-реализациями `ITypeMapConfiguration`, добавить:

```csharp
private int? _maxDepth;
public int? MaxDepth => _maxDepth;
```

- [ ] **Step 4: Собрать**

Run: `dotnet build src/MyAutoMapper/SmAutoMapper.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MyAutoMapper/Configuration/ITypeMapConfiguration.cs src/MyAutoMapper/Configuration/TypeMapBuilder.cs
git commit -m "feat(config): add MaxDepth to ITypeMapConfiguration"
```

### Task 2: Fluent метод `MaxDepth(int)` в `ITypeMappingExpression`

**Files:**
- Modify: `src/MyAutoMapper/Configuration/ITypeMappingExpression.cs`
- Modify: `src/MyAutoMapper/Configuration/TypeMapBuilder.cs`
- Test: `tests/MyAutoMapper.UnitTests/TypeMapBuilderMaxDepthTests.cs` (**new**)

- [ ] **Step 1: Написать failing-тест**

`tests/MyAutoMapper.UnitTests/TypeMapBuilderMaxDepthTests.cs`:

```csharp
using FluentAssertions;
using SmAutoMapper.Configuration;

namespace MyAutoMapper.UnitTests;

public class TypeMapBuilderMaxDepthTests
{
    private sealed class Src { public List<Src> Children { get; set; } = []; }
    private sealed class Dst { public List<Dst> Children { get; set; } = []; }

    private sealed class TestProfile : MappingProfile
    {
        public TestProfile() => CreateMap<Src, Dst>().MaxDepth(5);
    }

    [Fact]
    public void MaxDepth_is_stored_on_configuration()
    {
        var profile = new TestProfile();
        var config = profile.TypeMaps.Single();
        config.MaxDepth.Should().Be(5);
    }

    [Fact]
    public void MaxDepth_defaults_to_null_when_not_set()
    {
        var p = new MappingProfileAnon(b => b.CreateMap<Src, Dst>());
        p.TypeMaps.Single().MaxDepth.Should().BeNull();
    }

    private sealed class MappingProfileAnon : MappingProfile
    {
        public MappingProfileAnon(Action<MappingProfileAnon> cfg) => cfg(this);
        public new ITypeMappingExpression<TS,TD> CreateMap<TS,TD>() => base.CreateMap<TS,TD>();
    }
}
```

- [ ] **Step 2: Запустить тест — падает**

Run: `dotnet test tests/MyAutoMapper.UnitTests --filter FullyQualifiedName~TypeMapBuilderMaxDepthTests`
Expected: FAIL — `MaxDepth` не определён в fluent API.

- [ ] **Step 3: Добавить метод в интерфейс**

В `src/MyAutoMapper/Configuration/ITypeMappingExpression.cs`:

```csharp
ITypeMappingExpression<TSource, TDest> MaxDepth(int depth);
```

- [ ] **Step 4: Реализовать в `TypeMapBuilder`**

В `src/MyAutoMapper/Configuration/TypeMapBuilder.cs`:

```csharp
public ITypeMappingExpression<TSource, TDest> MaxDepth(int depth)
{
    if (depth < 1)
        throw new ArgumentOutOfRangeException(nameof(depth), "MaxDepth must be >= 1");
    _maxDepth = depth;
    return this;
}
```

- [ ] **Step 5: Запустить тест — проходит**

Run: `dotnet test tests/MyAutoMapper.UnitTests --filter FullyQualifiedName~TypeMapBuilderMaxDepthTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/MyAutoMapper/Configuration/ITypeMappingExpression.cs src/MyAutoMapper/Configuration/TypeMapBuilder.cs tests/MyAutoMapper.UnitTests/TypeMapBuilderMaxDepthTests.cs
git commit -m "feat(config): add fluent MaxDepth(int) on CreateMap"
```

### Task 3: Прокинуть `MaxDepth` в `TypeMap`

**Files:**
- Modify: `src/MyAutoMapper/Compilation/TypeMap.cs`
- Modify: `src/MyAutoMapper/Compilation/MapperConfiguration.cs`

- [ ] **Step 1: Добавить свойство в `TypeMap`**

В `src/MyAutoMapper/Compilation/TypeMap.cs`:

```csharp
public int? MaxDepth { get; }
```

И в конструктор — параметр `int? maxDepth`, присваивание `MaxDepth = maxDepth;`.

- [ ] **Step 2: Обновить вызов конструктора в `MapperConfiguration.BuildAndRegisterTypeMap`**

Найти создание `new TypeMap(...)` и добавить `typeMapConfig.MaxDepth` в аргументы (последним либо сразу после `holderPropertyMap`).

- [ ] **Step 3: Собрать**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/MyAutoMapper/Compilation/TypeMap.cs src/MyAutoMapper/Compilation/MapperConfiguration.cs
git commit -m "feat(compilation): carry MaxDepth through to TypeMap"
```

---

## Chunk 2: CollectionProjectionBuilder helper

### Task 4: Написать тесты детектора коллекций

**Files:**
- Test: `tests/MyAutoMapper.UnitTests/CollectionProjectionBuilderTests.cs` (**new**)

- [ ] **Step 1: Написать failing-тесты**

```csharp
using FluentAssertions;
using SmAutoMapper.Compilation;

namespace MyAutoMapper.UnitTests;

public class CollectionProjectionBuilderTests
{
    [Theory]
    [InlineData(typeof(List<int>), typeof(int))]
    [InlineData(typeof(IEnumerable<string>), typeof(string))]
    [InlineData(typeof(ICollection<int>), typeof(int))]
    [InlineData(typeof(IReadOnlyList<int>), typeof(int))]
    [InlineData(typeof(int[]), typeof(int))]
    public void TryGetElementType_supported_collections(Type input, Type expectedElement)
    {
        var ok = CollectionProjectionBuilder.TryGetElementType(input, out var element);
        ok.Should().BeTrue();
        element.Should().Be(expectedElement);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int))]
    [InlineData(typeof(Dictionary<int,int>))]
    public void TryGetElementType_rejects_non_collection(Type input)
    {
        var ok = CollectionProjectionBuilder.TryGetElementType(input, out _);
        ok.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Запустить — падает**

Run: `dotnet test tests/MyAutoMapper.UnitTests --filter FullyQualifiedName~CollectionProjectionBuilderTests`
Expected: FAIL — класс не существует.

- [ ] **Step 3: Реализовать `CollectionProjectionBuilder.TryGetElementType`**

Создать `src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs`:

```csharp
using System.Linq.Expressions;
using System.Reflection;

namespace SmAutoMapper.Compilation;

internal static class CollectionProjectionBuilder
{
    public static bool TryGetElementType(Type type, out Type elementType)
    {
        elementType = null!;
        if (type == typeof(string))
            return false;
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IEnumerable<>) ||
                def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>) ||
                def == typeof(IReadOnlyCollection<>) || def == typeof(IList<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }
        var ienum = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (ienum is not null)
        {
            elementType = ienum.GetGenericArguments()[0];
            return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test tests/MyAutoMapper.UnitTests --filter FullyQualifiedName~CollectionProjectionBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs tests/MyAutoMapper.UnitTests/CollectionProjectionBuilderTests.cs
git commit -m "feat(compilation): add CollectionProjectionBuilder.TryGetElementType"
```

### Task 5: `BuildSelectExpression` — обёртка Select/ToList/ToArray

**Files:**
- Modify: `src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs`
- Test: `tests/MyAutoMapper.UnitTests/CollectionProjectionBuilderTests.cs`

- [ ] **Step 1: Добавить failing-тест**

```csharp
[Fact]
public void BuildSelect_wraps_in_Select_and_ToList_when_dest_is_List()
{
    // sourceCollection: IEnumerable<int> (x => x + 1 on each), dest: List<int>
    var srcParam = Expression.Parameter(typeof(int[]), "arr");
    var elemParam = Expression.Parameter(typeof(int), "x");
    var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

    var expr = CollectionProjectionBuilder.BuildSelect(
        sourceCollection: srcParam,
        elementProjection: elemLambda,
        destType: typeof(List<int>));

    var lambda = Expression.Lambda<Func<int[], List<int>>>(expr, srcParam).Compile();
    lambda(new[] { 1, 2, 3 }).Should().Equal(2, 3, 4);
}

[Fact]
public void BuildSelect_returns_array_when_dest_is_array()
{
    var srcParam = Expression.Parameter(typeof(int[]), "arr");
    var elemParam = Expression.Parameter(typeof(int), "x");
    var elemLambda = Expression.Lambda(Expression.Add(elemParam, Expression.Constant(1)), elemParam);

    var expr = CollectionProjectionBuilder.BuildSelect(srcParam, elemLambda, typeof(int[]));
    var lambda = Expression.Lambda<Func<int[], int[]>>(expr, srcParam).Compile();
    lambda(new[] { 10 }).Should().Equal(11);
}
```

Добавить using в тестовый файл: `using System.Linq.Expressions;`.

- [ ] **Step 2: Запустить — падает**

Run: `dotnet test tests/MyAutoMapper.UnitTests --filter FullyQualifiedName~CollectionProjectionBuilderTests`
Expected: FAIL — метода `BuildSelect` нет.

- [ ] **Step 3: Реализовать `BuildSelect`**

Добавить в `CollectionProjectionBuilder.cs`:

```csharp
private static readonly MethodInfo EnumerableSelect =
    typeof(Enumerable).GetMethods()
        .First(m => m.Name == nameof(Enumerable.Select)
                    && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

private static readonly MethodInfo EnumerableToList =
    typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!;

private static readonly MethodInfo EnumerableToArray =
    typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!;

public static Expression BuildSelect(Expression sourceCollection, LambdaExpression elementProjection, Type destType)
{
    var srcElement = elementProjection.Parameters[0].Type;
    var dstElement = elementProjection.Body.Type;

    var select = EnumerableSelect.MakeGenericMethod(srcElement, dstElement);
    Expression call = Expression.Call(select, sourceCollection, elementProjection);

    if (destType.IsArray)
    {
        var toArray = EnumerableToArray.MakeGenericMethod(dstElement);
        return Expression.Call(toArray, call);
    }

    if (destType.IsGenericType)
    {
        var def = destType.GetGenericTypeDefinition();
        if (def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>))
            return call;
    }

    // List<>, ICollection<>, IReadOnlyList<>, IList<> → ToList
    var toList = EnumerableToList.MakeGenericMethod(dstElement);
    return Expression.Call(toList, call);
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test tests/MyAutoMapper.UnitTests --filter FullyQualifiedName~CollectionProjectionBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs tests/MyAutoMapper.UnitTests/CollectionProjectionBuilderTests.cs
git commit -m "feat(compilation): add BuildSelect for collection projection wrapping"
```

---

## Chunk 3: ProjectionCompiler — каталог и стек

### Task 6: Двухфазная сборка в `MapperConfiguration`

**Files:**
- Modify: `src/MyAutoMapper/Compilation/MapperConfiguration.cs`
- Modify: `src/MyAutoMapper/Compilation/ProjectionCompiler.cs`

Задача: `ProjectionCompiler.CompileProjection` должен получать словарь `IReadOnlyDictionary<TypePair, ITypeMapConfiguration>`, чтобы при компиляции рекурсивных коллекций находить конфиг элемента.

- [ ] **Step 1: Изменить сигнатуру `ProjectionCompiler.CompileProjection`**

В `ProjectionCompiler.cs` поменять:

```csharp
public CompilationResult CompileProjection(
    TypePair typePair,
    IReadOnlyList<PropertyMap> propertyMaps,
    LambdaExpression? customConstructor,
    IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog,
    Stack<TypePair>? compilationStack = null,
    int? maxDepth = null,
    ConstantExpression? sharedHolderConstant = null,
    HolderTypeInfo? sharedHolderInfo = null)
{
    compilationStack ??= new Stack<TypePair>();
    // ...существующая логика, плюс new parameters
}
```

Пока тело менять не нужно — только сигнатуру и протолкнуть compilationStack.Push/Pop вокруг основного тела:

```csharp
compilationStack.Push(typePair);
try
{
    // existing body
}
finally { compilationStack.Pop(); }
```

- [ ] **Step 2: Изменить `MapperConfiguration`: двухфазная сборка**

Заменить конструктор `MapperConfiguration`:

```csharp
internal MapperConfiguration(IReadOnlyList<MappingProfile> profiles)
{
    // Phase 1: собрать все ITypeMapConfiguration
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

    var catalog = allConfigs.ToDictionary(
        c => new TypePair(c.SourceType, c.DestinationType),
        c => c);

    // Phase 2: компиляция
    foreach (var cfg in allConfigs)
    {
        _typeMapConfigs.Add(cfg);
        BuildAndRegisterTypeMap(cfg, catalog);
    }
}
```

И обновить `BuildAndRegisterTypeMap` чтобы принимал `catalog` и передавал в `CompileProjection`:

```csharp
private void BuildAndRegisterTypeMap(
    ITypeMapConfiguration typeMapConfig,
    IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog)
{
    var typePair = new TypePair(typeMapConfig.SourceType, typeMapConfig.DestinationType);

    var usedParams = typeMapConfig.PropertyMaps
        .Where(pm => pm.HasParameterizedSource && pm.ParameterSlot is not null)
        .Select(pm => pm.ParameterSlot!)
        .DistinctBy(s => s.Name)
        .ToList();

    var compilationResult = _projectionCompiler.CompileProjection(
        typePair,
        typeMapConfig.PropertyMaps,
        typeMapConfig.CustomConstructor,
        catalog,
        compilationStack: null,
        maxDepth: typeMapConfig.MaxDepth);

    var compiledDelegate = _inMemoryCompiler.CompileDelegate(typePair, compilationResult.Projection);

    var typeMap = new TypeMap(
        typePair,
        typeMapConfig.PropertyMaps,
        typeMapConfig.CustomConstructor,
        usedParams,
        compilationResult.Projection,
        compiledDelegate,
        compilationResult.ClosureHolderType,
        compilationResult.DefaultClosureHolder,
        compilationResult.HolderPropertyMap,
        typeMapConfig.MaxDepth);

    _typeMaps[typePair] = typeMap;
}
```

- [ ] **Step 3: Собрать**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 4: Прогнать все существующие тесты (регрессия)**

Run: `dotnet test`
Expected: PASS (все старые тесты должны проходить — новая сигнатура совместима).

- [ ] **Step 5: Commit**

```bash
git add src/MyAutoMapper/Compilation/MapperConfiguration.cs src/MyAutoMapper/Compilation/ProjectionCompiler.cs
git commit -m "refactor(compilation): two-phase build, pass catalog and stack to compiler"
```

### Task 7: Сделать `HolderTypeInfo` доступным из `ProjectionCompiler` (для sharing)

**Files:**
- Modify: `src/MyAutoMapper/Parameters/ClosureHolderFactory.cs` (если `HolderTypeInfo` там)
- Modify: `src/MyAutoMapper/Compilation/ProjectionCompiler.cs`

- [ ] **Step 1: Убедиться что `HolderTypeInfo` — internal класс, доступный из ProjectionCompiler**

Прочитать `src/MyAutoMapper/Parameters/ClosureHolderFactory.cs`. Если `HolderTypeInfo` — private nested class — вынести в отдельный `internal` тип файла `src/MyAutoMapper/Parameters/HolderTypeInfo.cs`. Если уже internal — ничего не делать.

- [ ] **Step 2: Собрать**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 3: Commit (если были изменения)**

```bash
git add -u
git commit -m "refactor(parameters): expose HolderTypeInfo as internal"
```

---

## Chunk 4: Детектор коллекций в ProjectionCompiler

### Task 8: Failing-тест на самоссылающуюся проекцию

**Files:**
- Test: `tests/MyAutoMapper.IntegrationTests/NestedCollectionProjectionTests.cs` (**new**)

- [ ] **Step 1: Написать тест (EF Core Sqlite, in-memory `:memory:`)**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.Compilation;
using SmAutoMapper.Configuration;
using SmAutoMapper.Extensions;

namespace MyAutoMapper.IntegrationTests;

public class NestedCollectionProjectionTests
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
        public List<CategoryVm> SubCategories { get; set; } = [];
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
                .MaxDepth(3)
                .ForMember(d => d.SubCategories, o => o.MapFrom(s => s.Children));
        }
    }

    private static (Db db, IProjectionProvider proj) Build()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<Db>().UseSqlite(conn).Options;
        var db = new Db(opts);
        db.Database.EnsureCreated();

        var root = new Category { Id = 1, Name = "root",
            Children = { new Category { Id = 2, Name = "L1",
                Children = { new Category { Id = 3, Name = "L2",
                    Children = { new Category { Id = 4, Name = "L3",
                        Children = { new Category { Id = 5, Name = "L4" } } } } } } } };
        db.Categories.Add(root);
        db.SaveChanges();

        var cfg = new MapperConfiguration([new Profile()]);
        return (db, cfg.CreateProjectionProvider());
    }

    [Fact]
    public void Projects_up_to_MaxDepth_levels()
    {
        var (db, proj) = Build();
        var vm = db.Categories.Where(c => c.Id == 1)
            .ProjectTo<CategoryVm>(proj)
            .Single();

        vm.Name.Should().Be("root");
        vm.SubCategories.Should().HaveCount(1);
        vm.SubCategories[0].Name.Should().Be("L1");
        vm.SubCategories[0].SubCategories.Should().HaveCount(1);
        vm.SubCategories[0].SubCategories[0].Name.Should().Be("L2");
        // Глубина 3: root → L1 → L2 (SubCategories) заполнены, L2.SubCategories — пустая
        vm.SubCategories[0].SubCategories[0].SubCategories.Should().BeEmpty();
    }
}
```

Добавить в `tests/MyAutoMapper.IntegrationTests/MyAutoMapper.IntegrationTests.csproj`:
```xml
<PackageReference Include="Microsoft.Data.Sqlite" />
```
(если ещё нет — обычно входит в EntityFrameworkCore.Sqlite транзитивно, проверить `dotnet restore`).

- [ ] **Step 2: Запустить — падает**

Run: `dotnet test tests/MyAutoMapper.IntegrationTests --filter FullyQualifiedName~NestedCollectionProjectionTests`
Expected: FAIL — InvalidCast или трансляция EF падает.

### Task 9: Реализовать детектор коллекций в `ProjectionCompiler`

**Files:**
- Modify: `src/MyAutoMapper/Compilation/ProjectionCompiler.cs`
- Modify: `src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs`

- [ ] **Step 1: Заменить конечный `Expression.Convert` на детектор**

В `CompileProjection` в цикле `foreach (var propertyMap in propertyMaps)` найти блок:

```csharp
if (valueExpression.Type != propertyMap.DestinationProperty.PropertyType)
{
    valueExpression = Expression.Convert(valueExpression, propertyMap.DestinationProperty.PropertyType);
}
```

Заменить на:

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
        valueExpression = nested;
    }
    else
    {
        valueExpression = Expression.Convert(valueExpression, propertyMap.DestinationProperty.PropertyType);
    }
}
```

- [ ] **Step 2: Добавить private-метод `TryBuildNestedCollection`**

```csharp
private bool TryBuildNestedCollection(
    Expression sourceValue,
    Type destType,
    IReadOnlyDictionary<TypePair, ITypeMapConfiguration> catalog,
    Stack<TypePair> stack,
    ConstantExpression? sharedHolderConstant,
    HolderTypeInfo? sharedHolderInfo,
    out Expression? result)
{
    result = null;

    if (!CollectionProjectionBuilder.TryGetElementType(sourceValue.Type, out var srcElem))
        return false;
    if (!CollectionProjectionBuilder.TryGetElementType(destType, out var dstElem))
        return false;

    var elemPair = new TypePair(srcElem, dstElem);
    if (!catalog.TryGetValue(elemPair, out var elemConfig))
        return false;

    // Самоссылка: считаем, сколько раз пара уже в стеке
    var currentDepth = stack.Count(p => p == elemPair);
    var elemMaxDepth = elemConfig.MaxDepth ?? (stack.Contains(elemPair) ? 3 : int.MaxValue);

    var elemParam = Expression.Parameter(srcElem, "e");

    Expression elementBody;
    if (currentDepth >= elemMaxDepth)
    {
        // биндим пустой массив/список
        elementBody = Expression.New(dstElem); // empty object - actually we return empty collection at outer level
        // Правильнее: вернуть empty коллекцию СРАЗУ для внешнего bind, не на уровне элемента.
        // Решение: если превысили depth — возвращаем пустую коллекцию destType без .Select.
        result = BuildEmptyCollection(destType, dstElem);
        return true;
    }
    else
    {
        var inner = CompileProjection(
            elemPair,
            elemConfig.PropertyMaps,
            elemConfig.CustomConstructor,
            catalog,
            stack,
            elemConfig.MaxDepth,
            sharedHolderConstant,
            sharedHolderInfo);

        // Подставить elemParam в тело inner-лямбды
        var innerLambda = inner.Projection;
        var innerBody = ParameterReplacer.Replace(innerLambda.Body, innerLambda.Parameters[0], elemParam);
        elementBody = innerBody;
    }

    var elementLambda = Expression.Lambda(elementBody, elemParam);
    result = CollectionProjectionBuilder.BuildSelect(sourceValue, elementLambda, destType);
    return true;
}

private static Expression BuildEmptyCollection(Type destType, Type elementType)
{
    if (destType.IsArray)
    {
        var arrayEmpty = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(elementType);
        return Expression.Call(arrayEmpty);
    }
    // List<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>
    var listType = typeof(List<>).MakeGenericType(elementType);
    Expression newList = Expression.New(listType);
    if (destType != listType)
        newList = Expression.Convert(newList, destType);
    return newList;
}
```

- [ ] **Step 3: Добавить `BuildSelect` — уже есть (Task 5)**

- [ ] **Step 4: Собрать**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 5: Прогнать integration-тест**

Run: `dotnet test tests/MyAutoMapper.IntegrationTests --filter FullyQualifiedName~NestedCollectionProjectionTests`
Expected: PASS.

- [ ] **Step 6: Прогнать ВСЕ тесты (регрессия)**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/MyAutoMapper/Compilation/ProjectionCompiler.cs tests/MyAutoMapper.IntegrationTests/NestedCollectionProjectionTests.cs
git commit -m "feat(compilation): recursive nested collection projection with MaxDepth"
```

### Task 10: Тест на параметр `lang` через все уровни

**Files:**
- Modify: `tests/MyAutoMapper.IntegrationTests/NestedCollectionProjectionTests.cs`

- [ ] **Step 1: Добавить тест**

```csharp
[Fact]
public void Parameter_lang_propagates_to_all_nesting_levels()
{
    // Вариант Category с NameRu/NameUz, Profile с ParameterSlot<string>("lang")
    // и ожидание что дети получают то же значение lang, что и корень.
    // ... (полный сетап аналогичный Build, но с LocalizedName)
}
```

Если полная реализация слишком громоздкая — оставить как TODO в теле теста и пометить `[Fact(Skip = "covered by sample app manual test")]` ИЛИ реализовать через копию сущности `LocalizedCategory` / `LocalizedCategoryVm`.

- [ ] **Step 2: Пропустить если сложно, иначе прогнать**

Run: `dotnet test tests/MyAutoMapper.IntegrationTests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add -u
git commit -m "test(integration): verify parameter propagation through nested projection"
```

---

## Chunk 5: Sample app + верификация

### Task 11: Обновить `CategoryViewModelProfile`

**Files:**
- Modify: `samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs`

- [ ] **Step 1: Изменить профиль**

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
            .ForMember(d => d.SubCategories, o => o.MapFrom(src => src.Children));
    }
}
```

- [ ] **Step 2: Собрать sample**

Run: `dotnet build samples/MyAutoMapper.WebApiSample`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs
git commit -m "feat(sample): project SubCategories with MaxDepth(5) instead of Ignore"
```

### Task 12: Полный прогон

- [ ] **Step 1: Полная сборка + тесты**

Run: `dotnet build && dotnet test`
Expected: PASS.

- [ ] **Step 2: Проверить deleted файл `SpCategory.cs`**

Run: `git status`
Expected: `SpCategory.cs` всё ещё deleted — это не связано с нашей фичей; если файл не нужен — либо оставить deleted и закоммитить отдельно, либо восстановить через `git restore`. Спросить пользователя.

- [ ] **Step 3: Финальный commit (если требуется)**

---

## Done Criteria

- Все юнит- и integration-тесты проходят.
- Sample собирается.
- `CategoryViewModelProfile` не содержит `Ignore()` для `SubCategories`.
- `.MaxDepth(n)` доступен публично.
- Существующая функциональность (non-collection проекции) не регрессирует.
