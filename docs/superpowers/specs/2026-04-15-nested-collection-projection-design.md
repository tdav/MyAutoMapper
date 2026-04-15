# Nested Collection Projection with MaxDepth

**Date:** 2026-04-15
**Status:** Approved

## Problem

`ProjectionCompiler` не разворачивает вложенные маппинги коллекций. Для `Category.Children (List<Category>) → CategoryViewModel.SubCategories (List<CategoryViewModel>)` текущий код вставляет `Expression.Convert(src.Children, List<CategoryViewModel>)`, что приводит к `InvalidCastException` и несовместимо с EF Core IQueryable-проекцией.

## Goal

Поддержать автоматическое разворачивание коллекций в `IQueryable`-проекции: когда source/destination члены — коллекции типов, для которых зарегистрирован `TypeMap`, генерировать `src.Coll.Select(c => new Dest {...})`. Для самоссылающихся маппингов ограничивать глубину через конфигурируемый `MaxDepth`.

## Scope

- IQueryable (EF Core) проекция через `ProjectTo<T>()`.
- In-memory компиляция (`InMemoryCompiler`) — вне области этой фичи.

## Design

### Public API

```csharp
CreateMap<Category, CategoryViewModel>()
    .MaxDepth(5)
    .ForMember(d => d.LocalizedName, o => o.MapFrom(lang, (src, l) => ...));
// SubCategories: либо через явный ForMember(d => d.SubCategories, o => o.MapFrom(src => src.Children))
// либо через flattening-конвенцию (если поддерживает Children→SubCategories).
```

- `MaxDepth(int n)` — опционально. Дефолт для самоссылок — 3. Без самоссылки — не применяется.

### Behaviour

1. При компиляции проекции обнаружено несовпадение типов `valueExpression.Type != destProperty.PropertyType`.
2. Если обе стороны — коллекции (`IEnumerable<TS>` и `IEnumerable<TD>` / `List<>` / `ICollection<>` / `IReadOnlyList<>` / `T[]`) и есть `TypeMap(TS, TD)`:
   - Push `TypePair(TS,TD)` в стек компиляции.
   - Если пара уже в стеке и `currentDepth >= MaxDepth` → биндим пустую коллекцию (`new List<TD>()` или `Array.Empty<TD>()` с Convert).
   - Иначе рекурсивно компилируем element-projection с текущим стеком.
   - Оборачиваем: `src.Coll.Select(elementLambda)` + `.ToList()` если destination — `List<>`/конкретный класс-список.
   - Pop.
3. Holder-параметры (`lang`): вложенная компиляция переиспользует `holderConstant` внешнего вызова, чтобы одно значение параметра пробрасывалось через все уровни.

### Supported collection types

- Source: `IEnumerable<T>`, `List<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `T[]`.
- Destination: `List<T>`, `IEnumerable<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `T[]`.

Destination finalizer:
- `List<T>` / `ICollection<T>` / `IReadOnlyList<T>` → `.ToList()`.
- `T[]` → `.ToArray()`.
- `IEnumerable<T>` → без финализатора.

### Files to modify

| File | Change |
|------|--------|
| `src/MyAutoMapper/Configuration/ITypeMapConfiguration.cs` | add `int? MaxDepth { get; }` |
| `src/MyAutoMapper/Configuration/TypeMapBuilder.cs` | add fluent `MaxDepth(int)` |
| `src/MyAutoMapper/Compilation/TypeMap.cs` | store `MaxDepth` |
| `src/MyAutoMapper/Compilation/ProjectionCompiler.cs` | stack, holder-sharing, collection detection |
| `src/MyAutoMapper/Compilation/CollectionProjectionBuilder.cs` | **new** helper |
| `src/MyAutoMapper/Compilation/MapperConfiguration.cs` | pass TypeMap dictionary to compiler |
| `samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs` | remove `.Ignore()`, add `.MaxDepth(5)` |
| `tests/.../NestedCollectionProjectionTests.cs` | **new** integration test with EF Core Sqlite |

### Recursion / self-reference rule

Default `MaxDepth` for self-referencing `TypePair` = 3. User-specified value overrides. For non-self-referencing maps stack is used only to detect cycles; no depth limit imposed.

### Edge cases

- `src.Coll == null` in IQueryable: relies on SQL null semantics — не добавляем guard.
- In-memory path: не покрываем в этом spec.
- Пустая коллекция в конце рекурсии: `new List<TD>()` — материализуется в EF корректно.

## Out of scope

- In-memory (`Mapper.Map<>`) рекурсивное разворачивание.
- Custom element type converters.
- Циклы между разными типами (`A → B → A`) — работают тем же механизмом стека, но с одним общим `MaxDepth` на каждую пару.

## Acceptance

- `ProjectTo<CategoryViewModel>()` через EF Core Sqlite возвращает дерево до `MaxDepth` уровней.
- SQL генерируется без ошибок трансляции.
- Параметр `lang` одинаков на всех уровнях.
- Существующие тесты (non-collection проекции) не ломаются.
