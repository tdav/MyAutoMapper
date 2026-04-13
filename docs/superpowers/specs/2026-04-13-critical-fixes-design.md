# MyAutoMapper — Critical Fixes Design Spec

## Status
Approved

## Overview

Исправление 5 архитектурных проблем в MyAutoMapper, выявленных при ревью. Порядок реализации учитывает зависимости между изменениями.

## Approach

Поэтапный: каждое исправление — отдельный коммит. Порядок:
1. PropertyMap → record (меняет структуру данных, от которой зависят шаги 3-4)
2. FlatteningConvention null-safety
3. ConfigurationValidator + ILogger (unmapped property warnings)
4. ReverseMap() warnings (зависит от ILogger из шага 3)
5. Async в контроллерах (независимый, идёт последним)

---

## Step 1: PropertyMap → record

### Problem
`PropertyMap` — мутабельный class с `internal set`. `MemberMapBuilder` мутирует его после создания. Конфигурация, которая должна быть frozen после билда, остаётся мутабельной.

### Design

Заменить `PropertyMap` на immutable record:

```csharp
public sealed record PropertyMap(
    PropertyInfo DestinationProperty,
    LambdaExpression? SourceExpression = null,
    bool IsIgnored = false,
    bool HasParameterizedSource = false,
    IParameterSlot? ParameterSlot = null,
    LambdaExpression? ParameterizedSourceExpression = null);
```

### Impact on dependent files

**`MemberMapBuilder`** — становится аккумулятором значений вместо мутатора:

```csharp
internal sealed class MemberMapBuilder<TSource, TDest, TMember> : IMemberOptions<TSource, TDest, TMember>
{
    internal LambdaExpression? SourceExpression { get; private set; }
    internal bool IsIgnored { get; private set; }
    internal bool HasParameterizedSource { get; private set; }
    internal IParameterSlot? ParameterSlot { get; private set; }
    internal LambdaExpression? ParameterizedSourceExpression { get; private set; }

    public void MapFrom(Expression<Func<TSource, TMember>> sourceExpression)
        => SourceExpression = sourceExpression;

    public void MapFrom<TParam>(ParameterSlot<TParam> parameter, Expression<Func<TSource, TParam, TMember>> sourceExpression)
    {
        HasParameterizedSource = true;
        ParameterSlot = parameter;
        ParameterizedSourceExpression = sourceExpression;
    }

    public void Ignore() => IsIgnored = true;
}
```

**`TypeMapBuilder.ForMember()`** — создаёт `PropertyMap` после вызова `options()`:

```csharp
public ITypeMappingExpression<TSource, TDest> ForMember<TMember>(
    Expression<Func<TDest, TMember>> destinationMember,
    Action<IMemberOptions<TSource, TDest, TMember>> options)
{
    var propertyInfo = ExtractPropertyInfo(destinationMember);
    var builder = new MemberMapBuilder<TSource, TDest, TMember>();
    options(builder);
    var propertyMap = new PropertyMap(
        propertyInfo,
        SourceExpression: builder.SourceExpression,
        IsIgnored: builder.IsIgnored,
        HasParameterizedSource: builder.HasParameterizedSource,
        ParameterSlot: builder.ParameterSlot,
        ParameterizedSourceExpression: builder.ParameterizedSourceExpression);
    _propertyMaps.Add(propertyMap);
    return this;
}
```

**`TypeMapBuilder.Ignore()`** — используем named arguments:
```csharp
var propertyMap = new PropertyMap(propertyInfo, IsIgnored: true);
```

**`TypeMapBuilder.ReverseMap()`** — аналогично:
```csharp
var reversePropertyMap = new PropertyMap(destPropertyOnReverse, SourceExpression: reverseSourceExpr);
```

**`ProjectionCompiler`** — только читает `PropertyMap`, изменений не нужно.

### Files modified
- `src/MyAutoMapper/Configuration/PropertyMap.cs`
- `src/MyAutoMapper/Configuration/MemberMapBuilder.cs`
- `src/MyAutoMapper/Configuration/TypeMapBuilder.cs`

---

## Step 2: FlatteningConvention null-safety

### Problem
`TryFlatten` строит цепочку `Expression.Property(currentExpression, property)` без null-проверок. При `src.Address.City`, если `Address == null` — `NullReferenceException` в runtime.

### Design

В методе `TryFlatten`, когда рекурсивный вызов возвращает `deeper is not null`, оборачиваем результат в conditional expression:

```csharp
var deeper = TryFlatten(property.PropertyType, remaining, destPropertyType, propertyAccess, depth + 1);
if (deeper is not null)
{
    // Wrap with null-check for reference-type intermediate properties
    return Expression.Condition(
        Expression.Equal(propertyAccess, Expression.Constant(null, property.PropertyType)),
        Expression.Default(destPropertyType),
        deeper);
}
```

### Notes
- Проверка нужна только для ссылочных типов. Value types уже отсечены фильтром `!property.PropertyType.IsValueType` на строке 48.
- `Expression.Condition` транслируется в SQL `CASE WHEN ... IS NULL THEN NULL ELSE ... END` — полностью совместимо с EF Core.
- Для in-memory маппинга (compiled delegate) тоже работает корректно.

### Files modified
- `src/MyAutoMapper/Compilation/Conventions/FlatteningConvention.cs`

---

## Step 3: ConfigurationValidator + ILogger (unmapped property warnings)

### Problem
Цикл в `ConfigurationValidator` (строки 61-78) находит немаппированные destination properties, но тело цикла пустое. Пользователь не получает обратной связи о свойствах, которые получат default-значения.

### Design

1. `ConfigurationValidator` получает `ILogger<ConfigurationValidator>?` через конструктор (nullable для работы без DI):

```csharp
internal sealed class ConfigurationValidator
{
    private readonly ILogger<ConfigurationValidator>? _logger;

    public ConfigurationValidator(ILogger<ConfigurationValidator>? logger = null)
    {
        _logger = logger;
    }
    // ...
}
```

2. В пустом цикле (строка 77) добавляем warning:

```csharp
_logger?.LogWarning(
    "Mapping {Mapping}: destination property '{Property}' is not mapped and will receive default value",
    mappingName, destProp.Name);
```

3. В `ServiceCollectionExtensions` — получаем logger через временный ServiceProvider:

```csharp
var configuration = builder.Build();

using var tempSp = services.BuildServiceProvider();
var logger = tempSp.GetService<ILoggerFactory>()?.CreateLogger<ConfigurationValidator>();
var validator = new ConfigurationValidator(logger);
validator.Validate(configuration.GetAllTypeMaps());
```

### Notes
- `BuildServiceProvider` на startup — стандартный паттерн для eager validation.
- `ILoggerFactory` уже зарегистрирован фреймворком к моменту вызова `AddMapping()`.
- Nullable logger позволяет использовать валидатор без DI (тесты, standalone).

### Files modified
- `src/MyAutoMapper/Validation/ConfigurationValidator.cs`
- `src/MyAutoMapper/Extensions/ServiceCollectionExtensions.cs`

---

## Step 4: ReverseMap() warnings

### Problem
`TypeMapBuilder.ReverseMap()` молча пропускает маппинги: ignored, parameterized, вычисляемые (body не `MemberExpression`), readonly source properties. Пользователь не знает, что reverse map неполный.

### Design

1. В `TypeMapBuilder.ReverseMap()` собираем список пропущенных свойств:

```csharp
private readonly List<string> _skippedReverseProperties = [];

public ITypeMappingExpression<TDest, TSource> ReverseMap()
{
    _reverseMap = new TypeMapBuilder<TDest, TSource>();

    foreach (var propertyMap in _propertyMaps)
    {
        if (propertyMap.IsIgnored)
        {
            _skippedReverseProperties.Add($"{propertyMap.DestinationProperty.Name} (ignored)");
            continue;
        }
        if (propertyMap.HasParameterizedSource)
        {
            _skippedReverseProperties.Add($"{propertyMap.DestinationProperty.Name} (parameterized)");
            continue;
        }
        // ... existing MemberExpression check ...
        // else:
        _skippedReverseProperties.Add($"{propertyMap.DestinationProperty.Name} (computed/readonly)");
    }
    return _reverseMap;
}
```

2. Добавить `SkippedReverseProperties` в `ITypeMapConfiguration`:

```csharp
IReadOnlyList<string> SkippedReverseProperties { get; }
```

3. В `ConfigurationValidator` при валидации reverse map логировать:

```csharp
if (typeMapConfig.SkippedReverseProperties.Count > 0)
{
    _logger?.LogWarning(
        "ReverseMap {Mapping}: properties [{Properties}] were skipped (computed/parameterized/readonly)",
        mappingName, string.Join(", ", typeMapConfig.SkippedReverseProperties));
}
```

### Notes
- Логирование через уже добавленный `ILogger` из шага 3 — консистентно.
- `SkippedReverseProperties` — readonly, заполняется только в `ReverseMap()`.
- Валидатору нужен доступ к `ITypeMapConfiguration`, а не только к `TypeMap`. Нужно передавать конфигурации в валидатор дополнительно, либо хранить ссылку на `ITypeMapConfiguration` в `TypeMap`.

### Design decision: как валидатор получает SkippedReverseProperties

`MappingConfigurationBuilder` уже создаёт `ITypeMapConfiguration` объекты (из профилей) и передаёт их в `MapperConfiguration.Build()`. Нужно:

1. `MapperConfiguration` сохраняет `IReadOnlyList<ITypeMapConfiguration>` и выставляет через `GetAllTypeMapConfigurations()`.
2. Валидатор получает перегрузку:
```csharp
public void Validate(IReadOnlyCollection<TypeMap> typeMaps, IReadOnlyCollection<ITypeMapConfiguration> configs)
```
3. В `ServiceCollectionExtensions` передаём оба списка:
```csharp
validator.Validate(configuration.GetAllTypeMaps(), configuration.GetAllTypeMapConfigurations());
```

### Files modified
- `src/MyAutoMapper/Configuration/TypeMapBuilder.cs`
- `src/MyAutoMapper/Configuration/ITypeMapConfiguration.cs`
- `src/MyAutoMapper/Validation/ConfigurationValidator.cs`
- `src/MyAutoMapper/Compilation/MapperConfiguration.cs`
- `src/MyAutoMapper/Extensions/ServiceCollectionExtensions.cs`

---

## Step 5: Async в контроллерах (samples)

### Problem
`ProductsController` — все action-методы синхронные, используют `.ToList()` и `.FirstOrDefault()`. В ASP.NET Core это блокирует потоки пула.

### Design

Простая замена:
- `public IActionResult Method(...)` → `public async Task<IActionResult> Method(...)`
- `.ToList()` → `await ...ToListAsync()`
- `.FirstOrDefault()` → `await ...FirstOrDefaultAsync()`
- Добавить `using Microsoft.EntityFrameworkCore;`

### Files modified
- `samples/MyAutoMapper.WebApiSample/Controllers/ProductsController.cs`

---

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| PropertyMap → record ломает бинарную совместимость | Библиотека pre-release, breaking changes допустимы |
| `BuildServiceProvider` на startup создаёт временный контейнер | Стандартный паттерн, минимальный overhead на startup |
| Null-check в flattening меняет поведение Expression Tree | EF Core корректно транслирует `Expression.Condition`; покрыть тестами |
| `SkippedReverseProperties` усложняет `ITypeMapConfiguration` | Минимальное дополнение (одно readonly свойство) |

## Testing Strategy

Каждый шаг должен сопровождаться unit-тестами:
1. PropertyMap record — проверить что immutable (компилятор гарантирует)
2. FlatteningConvention — тест с null intermediate property, проверить default вместо NRE
3. Validator warnings — тест с unmapped property, проверить что logger вызывается
4. ReverseMap warnings — тест с computed mapping, проверить SkippedReverseProperties
5. Async — integration test (уже существующий) должен продолжать проходить
