# MyAutoMapper

> **[English version (README.md)](README.md)**

Легковесная высокопроизводительная библиотека маппинга объектов для **.NET 10** с первоклассной поддержкой **параметризованных EF Core проекций**.

Единственная внешняя зависимость — `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Оглавление

- [Возможности](#возможности)
- [Требования](#требования)
- [Структура проекта](#структура-проекта)
- [Архитектура](#архитектура)
  - [Слой конфигурации (Configuration)](#1-слой-конфигурации-configuration)
  - [Слой компиляции (Compilation)](#2-слой-компиляции-compilation)
  - [Слой параметров (Parameters)](#3-слой-параметров-parameters)
  - [Слой выполнения (Runtime)](#4-слой-выполнения-runtime)
  - [Расширения (Extensions)](#5-расширения-extensions)
  - [Валидация (Validation)](#6-валидация-validation)
- [Как работает маппинг — пошагово](#как-работает-маппинг--пошагово)
- [Как работают параметризованные проекции — пошагово](#как-работают-параметризованные-проекции--пошагово)
- [Конвенции автоматического маппинга](#конвенции-автоматического-маппинга)
- [Fluent API](#fluent-api)
- [Интеграция с DI](#интеграция-с-di)
- [Пример: Web API с локализацией](#пример-web-api-с-локализацией)
- [Тестирование](#тестирование)
- [Бенчмарки](#бенчмарки)
- [Сборка и запуск](#сборка-и-запуск)

---

## Возможности

| Возможность | Описание |
|---|---|
| **Expression-проекции** | Генерирует `Expression<Func<TSource, TDest>>` для EF Core `IQueryable` — в SQL попадают только нужные столбцы |
| **Параметризованные проекции** | `ParameterSlot<T>` использует closure-паттерн, который EF Core транслирует в SQL-параметры (`@__param_0`), сохраняя кэш планов запросов |
| **In-memory маппинг** | Скомпилированные `Func<TSource, TDest>` делегаты для быстрого маппинга объект-в-объект |
| **Конвенции** | Автоматический маппинг по совпадению имён + flattening вложенных объектов (`Address.City` -> `AddressCity`) |
| **Fluent API** | `CreateMap<S, D>().ForMember(...).Ignore(...).ConstructUsing(...).ReverseMap()` |
| **Eager-валидация** | Все маппинги проверяются при старте; ошибки выбрасываются сразу с полным списком проблем |
| **DI-интеграция** | `services.AddMapping()` со сканированием сборок, синглтон-регистрацией |

## Требования

- .NET 10 SDK (10.0.201+)
- C# 14 (`LangVersion preview`)

## Структура проекта

```
MyAutoMapper/
├── MyAutoMapper.slnx                      # .NET 10 XML solution
├── Directory.Build.props                  # net10.0, C# 14, Nullable, TreatWarningsAsErrors
├── Directory.Packages.props               # Central Package Management
│
├── src/MyAutoMapper/                      # Основная библиотека
│   ├── Configuration/                     # Fluent API, профили, билдеры
│   │   ├── MappingProfile.cs              # Абстрактный базовый класс профиля
│   │   ├── ITypeMappingExpression.cs      # Fluent-интерфейс для CreateMap
│   │   ├── IMemberOptions.cs              # Fluent-интерфейс для ForMember
│   │   ├── TypeMapBuilder.cs              # Реализация fluent-конфигурации
│   │   ├── MemberMapBuilder.cs            # Конфигурация отдельного свойства
│   │   ├── PropertyMap.cs                 # Метаданные маппинга одного свойства
│   │   ├── MappingConfigurationBuilder.cs # Накопление профилей, Build()
│   │   └── ITypeMapConfiguration.cs       # Non-generic интерфейс метаданных
│   │
│   ├── Compilation/                       # Движок Expression Tree
│   │   ├── ProjectionCompiler.cs          # Построение Expression<Func<S,D>>
│   │   ├── InMemoryCompiler.cs            # Компиляция в Func<S,D> делегат
│   │   ├── ParameterReplacer.cs           # ExpressionVisitor: замена параметров
│   │   ├── ClosureValueInjector.cs        # ExpressionVisitor: подмена closure holder
│   │   ├── MapperConfiguration.cs         # Frozen синглтон, ConcurrentDictionary
│   │   ├── TypeMap.cs                     # Immutable контейнер скомпилированного маппинга
│   │   ├── TypePair.cs                    # readonly record struct — ключ кэша
│   │   └── Conventions/                   # Конвенции автоматического маппинга
│   │       ├── INameConvention.cs         # Интерфейс конвенции
│   │       ├── DefaultNameConvention.cs   # Совпадение имён (case-insensitive)
│   │       └── FlatteningConvention.cs    # Разворачивание вложенных объектов
│   │
│   ├── Parameters/                        # Параметризация проекций
│   │   ├── ParameterSlot.cs               # Объявление параметра в профиле
│   │   ├── IParameterSlot.cs              # Non-generic интерфейс слота
│   │   ├── IParameterBinder.cs            # Интерфейс привязки значений
│   │   ├── ParameterBinder.cs             # Реализация — словарь параметров
│   │   └── ClosureHolderFactory.cs        # Генерация POCO через Reflection.Emit
│   │
│   ├── Runtime/                           # Маппинг в runtime
│   │   ├── IMapper.cs                     # Интерфейс in-memory маппера
│   │   ├── Mapper.cs                      # Вызов скомпилированного делегата
│   │   ├── IProjectionProvider.cs         # Интерфейс провайдера проекций
│   │   ├── ProjectionProvider.cs          # Выдача Expression + инъекция параметров
│   │   └── MappingContext.cs              # Key/value контекст
│   │
│   ├── Extensions/                        # Extension-методы
│   │   ├── ServiceCollectionExtensions.cs # AddMapping() для DI
│   │   └── QueryableExtensions.cs         # ProjectTo<S,D>() для IQueryable
│   │
│   └── Validation/                        # Валидация конфигурации
│       ├── ConfigurationValidator.cs      # Проверка типов, unmapped свойств
│       └── MappingValidationException.cs  # Исключение со списком ошибок
│
├── samples/
│   └── MyAutoMapper.WebApiSample/         # Пример ASP.NET Core Web API
│
├── tests/
│   ├── MyAutoMapper.UnitTests/            # Unit-тесты (xUnit)
│   ├── MyAutoMapper.IntegrationTests/     # EF Core SQLite интеграционные тесты
│   └── MyAutoMapper.Benchmarks/           # BenchmarkDotNet (vs AutoMapper, Mapster)
```

---

## Архитектура

Библиотека состоит из 6 слоёв. Каждый слой имеет одну чёткую ответственность.

### 1. Слой конфигурации (Configuration)

**Задача**: собрать метаданные маппинга через Fluent API.

#### MappingProfile

Абстрактный базовый класс. Пользователь наследуется от него и описывает маппинги в конструкторе:

```csharp
public abstract class MappingProfile
{
    internal List<ITypeMapConfiguration> TypeMaps { get; } = [];

    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>();
    protected ParameterSlot<T> DeclareParameter<T>(string name);
}
```

- `CreateMap<S, D>()` — создаёт `TypeMapBuilder<S, D>`, добавляет в `TypeMaps` и возвращает fluent-интерфейс.
- `DeclareParameter<T>(name)` — создаёт `ParameterSlot<T>` для использования в параметризованных маппингах.

#### TypeMapBuilder&lt;TSource, TDest&gt;

Реализует одновременно `ITypeMappingExpression<S, D>` (для fluent API) и `ITypeMapConfiguration` (для передачи компилятору). Внутри накапливает список `PropertyMap`:

```csharp
internal sealed class TypeMapBuilder<TSource, TDest> : ITypeMappingExpression<TSource, TDest>, ITypeMapConfiguration
{
    private readonly List<PropertyMap> _propertyMaps = [];
    private LambdaExpression? _customConstructor;
    private TypeMapBuilder<TDest, TSource>? _reverseMap;
}
```

**Извлечение PropertyInfo** из лямбда-выражений: метод `ExtractPropertyInfo` обрабатывает `UnaryExpression` (для value-типов, оборачиваемых в `Convert`) и `MemberExpression`.

#### MemberMapBuilder&lt;TSource, TDest, TMember&gt;

Реализует `IMemberOptions<S, D, M>`. Два варианта `MapFrom`:

```csharp
// Обычный маппинг
void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

// Параметризованный маппинг
void MapFrom<TParam>(ParameterSlot<TParam> parameter,
    Expression<Func<TSource, TParam, TMember>> sourceExpression);
```

Параметризованный `MapFrom` сохраняет в `PropertyMap`:
- `HasParameterizedSource = true`
- `ParameterSlot` — ссылка на слот
- `ParameterizedSourceExpression` — лямбда с двумя параметрами `(src, paramValue)`

#### PropertyMap

Хранит метаданные маппинга одного свойства:

```csharp
public sealed class PropertyMap
{
    public PropertyInfo DestinationProperty { get; }
    public LambdaExpression? SourceExpression { get; }      // обычный MapFrom
    public bool IsIgnored { get; }
    public bool HasParameterizedSource { get; }              // параметризованный MapFrom
    public IParameterSlot? ParameterSlot { get; }
    public LambdaExpression? ParameterizedSourceExpression { get; }
}
```

#### MappingConfigurationBuilder

Накапливает профили и вызывает `Build()`:

```csharp
var builder = new MappingConfigurationBuilder();
builder.AddProfile<MyProfile>();              // явный профиль
builder.AddProfiles(typeof(X).Assembly);      // сканирование сборки
var config = builder.Build();                 // → MapperConfiguration (singleton)
```

---

### 2. Слой компиляции (Compilation)

**Задача**: трансформировать метаданные `PropertyMap` в `Expression<Func<S, D>>` и `Func<S, D>`.

#### ProjectionCompiler

Центральный класс. Алгоритм:

1. Создаёт `ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "src")`
2. Для каждого `PropertyMap` строит `MemberBinding`:
   - **Параметризованный маппинг**: подставляет `holder.property` (closure-паттерн) через `ParameterReplacer`
   - **Обычный MapFrom**: подставляет `sourceParam` в пользовательскую лямбду через `ParameterReplacer`
   - **Конвенция**: пробует `DefaultNameConvention`, затем `FlatteningConvention`
3. Автоматически применяет конвенции для всех свойств назначения, не указанных в `ForMember`
4. Собирает `Expression.MemberInit(Expression.New(typeof(TDest)), bindings)`
5. Оборачивает в `Expression.Lambda<Func<S, D>>(body, sourceParam)`

Возвращает неизменяемый `CompilationResult`:

```csharp
internal sealed record CompilationResult(
    LambdaExpression Projection,
    Type? ClosureHolderType,
    object? DefaultClosureHolder,
    IReadOnlyDictionary<string, PropertyInfo>? HolderPropertyMap);
```

#### ParameterReplacer

Простой `ExpressionVisitor`, заменяющий один `ParameterExpression` на другое `Expression`:

```csharp
// Заменяет параметр пользовательской лямбды на глобальный sourceParam
body = ParameterReplacer.Replace(lambda.Body, lambda.Parameters[0], sourceParam);
```

Используется в двух сценариях:
- Унификация `sourceParam` из разных лямбд `MapFrom` в единое выражение
- Замена параметра `TParam` на `MemberAccess(holder, property)` для параметризации

#### InMemoryCompiler

Принимает `LambdaExpression`, оборачивает в null-check и компилирует:

```csharp
// Если source != null → вычислить проекцию, иначе → default(TDest)
Expression.Condition(
    Expression.Equal(sourceParam, Expression.Constant(null)),
    Expression.Default(destType),
    projectionExpr.Body);
```

Вызывает `Expression.Compile()` для получения `Func<S, D>` делегата.

#### ClosureValueInjector

`ExpressionVisitor`, который заменяет `ConstantExpression` с типом closure holder на новый экземпляр с обновлёнными значениями параметров:

```csharp
protected override Expression VisitConstant(ConstantExpression node)
{
    if (node.Type == _holderType)
        return Expression.Constant(_newHolderInstance, _holderType);
    return base.VisitConstant(node);
}
```

**Ключевое свойство**: форма дерева выражений не меняется — меняется только значение внутри `ConstantExpression`. EF Core видит идентичную структуру и переиспользует кэшированный план запроса.

#### MapperConfiguration

Frozen-синглтон. В конструкторе итерирует все профили, вызывает `ProjectionCompiler.CompileProjection()` и `InMemoryCompiler.CompileDelegate()`, строит неизменяемые `TypeMap` и сохраняет в `ConcurrentDictionary<TypePair, TypeMap>`:

```csharp
public sealed class MapperConfiguration
{
    private readonly ConcurrentDictionary<TypePair, TypeMap> _typeMaps = new();

    public IMapper CreateMapper();
    public IProjectionProvider CreateProjectionProvider();
    public TypeMap GetTypeMap<TSource, TDest>();
}
```

#### TypeMap

Полностью неизменяемый контейнер скомпилированного маппинга. Все свойства — `get`-only, устанавливаются в конструкторе:

```csharp
public sealed class TypeMap
{
    public TypePair TypePair { get; }
    public IReadOnlyList<PropertyMap> PropertyMaps { get; }
    public LambdaExpression? ProjectionExpression { get; }   // для EF Core
    public Delegate? CompiledDelegate { get; }               // для in-memory
    public Type? ClosureHolderType { get; }                  // для параметризации
    public object? DefaultClosureHolder { get; }
    internal IReadOnlyDictionary<string, PropertyInfo>? HolderPropertyMap { get; }
}
```

`HolderPropertyMap` кэширует `PropertyInfo` словарь, чтобы при каждом запросе не вызывать `Type.GetProperty()` через reflection.

#### TypePair

Ключ для `ConcurrentDictionary`:

```csharp
public readonly record struct TypePair(Type SourceType, Type DestinationType);
```

---

### 3. Слой параметров (Parameters)

**Задача**: реализовать параметризованные проекции через closure-паттерн, который EF Core нативно переводит в SQL-параметры.

#### ParameterSlot&lt;T&gt;

Объявление параметра в профиле:

```csharp
public sealed class ParameterSlot<T> : IParameterSlot
{
    public string Name { get; }
    public Type ValueType => typeof(T);
    public Guid Id { get; } = Guid.NewGuid();
}
```

#### ClosureHolderFactory

Динамически генерирует POCO-тип через `System.Reflection.Emit` (`TypeBuilder`, `FieldBuilder`, `PropertyBuilder`, `ILGenerator`):

```
ParameterSlot<string>("lang") + ParameterSlot<int>("limit")
    ↓ ClosureHolderFactory
class ClosureHolder_1 {
    public string lang { get; set; }
    public int limit { get; set; }
}
```

Алгоритм:
1. `AssemblyBuilder.DefineDynamicAssembly()` — один раз, статический
2. `ModuleBuilder.DefineType()` — для каждого уникального набора параметров
3. Для каждого слота: `DefineField` + `DefineProperty` + getter IL (`Ldarg_0`, `Ldfld`, `Ret`) + setter IL (`Ldarg_0`, `Ldarg_1`, `Stfld`, `Ret`)
4. `TypeBuilder.CreateType()` — финализация
5. Результат кэшируется в `ConcurrentDictionary` по ключу `"name:type|name:type"`

Возвращает `HolderTypeInfo`:
```csharp
internal sealed class HolderTypeInfo
{
    public Type HolderType { get; }
    public IReadOnlyDictionary<string, PropertyInfo> PropertyMap { get; }
    public object CreateInstance(IReadOnlyDictionary<string, object?> values);
    public object CreateDefaultInstance();
}
```

#### ParameterBinder

Реализация `IParameterBinder` — простой словарь для передачи значений при запросе:

```csharp
public sealed class ParameterBinder : IParameterBinder
{
    private readonly Dictionary<string, object?> _values = [];

    public IParameterBinder Set<T>(string name, T value);
    public IParameterBinder Set<T>(ParameterSlot<T> slot, T value);
    public IReadOnlyDictionary<string, object?> Values { get; }
}
```

---

### 4. Слой выполнения (Runtime)

**Задача**: предоставить `IMapper` для in-memory маппинга и `IProjectionProvider` для EF Core проекций.

#### Mapper

Stateless. Получает `TypeMap` из `MapperConfiguration`, кастит `CompiledDelegate` к `Func<S, D>` и вызывает:

```csharp
public TDest Map<TSource, TDest>(TSource source)
{
    var typeMap = _configuration.GetTypeMap<TSource, TDest>();
    var func = (Func<TSource, TDest>)typeMap.CompiledDelegate;
    return func(source);
}
```

Производительность — наносекунды (один вызов скомпилированного делегата).

#### ProjectionProvider

Два режима:
- **Без параметров**: возвращает кэшированное `Expression<Func<S, D>>` из `TypeMap`
- **С параметрами**:
  1. Создаёт новый экземпляр closure holder через `Activator.CreateInstance()`
  2. Заполняет свойства из `ParameterBinder.Values` используя кэшированный `HolderPropertyMap`
  3. Вызывает `ClosureValueInjector.InjectParameters()` — заменяет `ConstantExpression` в дереве
  4. Возвращает новое выражение с той же структурой, но новыми значениями

```csharp
public Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>(IParameterBinder parameters)
{
    var newHolder = Activator.CreateInstance(holderType);
    foreach (var (name, value) in parameters.Values)
        typeMap.HolderPropertyMap[name].SetValue(newHolder, value);

    return ClosureValueInjector.InjectParameters<TSource, TDest>(
        templateExpression, holderType, newHolder);
}
```

---

### 5. Расширения (Extensions)

#### ServiceCollectionExtensions

```csharp
public static IServiceCollection AddMapping(
    this IServiceCollection services,
    Action<MappingConfigurationBuilder>? configure = null,
    params Assembly[] profileAssemblies)
```

Что делает:
1. Создаёт `MappingConfigurationBuilder`
2. Вызывает `configure?.Invoke(builder)` для ручной конфигурации
3. Сканирует переданные сборки: находит все наследники `MappingProfile`
4. Вызывает `builder.Build()` — компилирует все маппинги
5. Запускает `ConfigurationValidator.Validate()` — eager-валидация
6. Регистрирует как **Singleton**: `MapperConfiguration`, `IMapper`, `IProjectionProvider`

#### QueryableExtensions

```csharp
// Без параметров
source.ProjectTo<Product, ProductDto>(projections);

// С параметрами
source.ProjectTo<Product, ProductDto>(projections,
    p => p.Set("lang", "ru"));
```

Внутри вызывает `provider.GetProjection<S, D>()` и `source.Select(expression)`.

---

### 6. Валидация (Validation)

#### ConfigurationValidator

Проверяет при старте:
- Совместимость типов для всех явных `ForMember` маппингов
- Обнаруживает числовые конверсии (`int` -> `long`, `float` -> `double` и т.д.)
- Обрабатывает nullable-обёртки (`int` -> `int?`)
- Не предупреждает о свойствах, которые могут быть разрешены конвенциями

#### MappingValidationException

Выбрасывается при ошибках с полным списком:

```
Mapping configuration validation failed with 2 error(s):
  1. [Product -> ProductDto] Property 'Price': source type 'String' is not assignable to destination type 'Decimal'.
  2. [Order -> OrderDto] Property 'Status': source type 'Int32' is not assignable to destination type 'String'.
```

---

## Как работает маппинг — пошагово

### Этап конфигурации (один раз при старте)

```
1. Пользователь: CreateMap<Product, ProductDto>()
                    .ForMember(d => d.Name, o => o.MapFrom(s => s.Title))

2. TypeMapBuilder:
   PropertyMap { DestinationProperty: "Name", SourceExpression: s => s.Title }

3. MappingConfigurationBuilder.Build() → MapperConfiguration:

4. ProjectionCompiler.CompileProjection():
   4a. sourceParam = Expression.Parameter(typeof(Product), "src")
   4b. ForMember "Name": ParameterReplacer(s => s.Title, s → src) → src.Title
   4c. Конвенция "Id": DefaultNameConvention → src.Id
   4d. Конвенция "Price": DefaultNameConvention → src.Price
   4e. MemberInit: new ProductDto { Name = src.Title, Id = src.Id, Price = src.Price }
   4f. Lambda: src => new ProductDto { Name = src.Title, Id = src.Id, Price = src.Price }

5. InMemoryCompiler.CompileDelegate():
   5a. Null-check обёртка
   5b. Expression.Compile() → Func<Product, ProductDto>

6. TypeMap: хранит и Expression, и Delegate
```

### Этап выполнения (каждый вызов)

```
// In-memory
mapper.Map<Product, ProductDto>(product)
  → TypeMap.CompiledDelegate → (Func<Product, ProductDto>)(product) → dto

// EF Core
dbContext.Products.ProjectTo<Product, ProductDto>(projections)
  → provider.GetProjection<Product, ProductDto>()
  → Expression<Func<Product, ProductDto>>
  → source.Select(expression)
  → EF Core транслирует в SQL:
    SELECT [p].[Title] AS [Name], [p].[Id], [p].[Price] FROM [Products] AS [p]
```

---

## Как работают параметризованные проекции — пошагово

Это ключевая инновация библиотеки. Обычные подходы (string interpolation, замена констант) ломают кэш планов запросов EF Core. MyAutoMapper использует closure-паттерн — тот же, что C# компилятор генерирует для замыканий.

### Этап конфигурации

```
1. Пользователь:
   var lang = DeclareParameter<string>("lang");
   CreateMap<Product, ProductViewModel>()
       .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
           (src, l) => l == "ru" ? src.NameRu : src.NameUz))

2. ClosureHolderFactory (Reflection.Emit):
   Генерирует динамический тип:
   class ClosureHolder_1 { public string lang { get; set; } }

3. ProjectionCompiler:
   3a. holderInstance = new ClosureHolder_1()   // экземпляр по умолчанию
   3b. holderConstant = Expression.Constant(holderInstance, typeof(ClosureHolder_1))
   3c. holderPropertyAccess = Expression.Property(holderConstant, "lang")
       // это MemberAccess(Constant(holder), "lang") — closure-паттерн!
   3d. Подставляет в пользовательскую лямбду:
       l → holderPropertyAccess
       src → sourceParam
   3e. Результат:
       src => holderInstance.lang == "ru" ? src.NameRu : src.NameUz
```

### Этап запроса

```
1. Пользователь:
   dbContext.Products.ProjectTo<Product, ProductViewModel>(projections,
       p => p.Set("lang", "ru"))

2. ParameterBinder: { "lang": "ru" }

3. ProjectionProvider:
   3a. newHolder = new ClosureHolder_1()
   3b. newHolder.lang = "ru"  // через кэшированный HolderPropertyMap
   3c. ClosureValueInjector.InjectParameters():
       Обходит дерево, находит ConstantExpression(oldHolder) → заменяет на ConstantExpression(newHolder)
   3d. Форма дерева ИДЕНТИЧНА — изменилось только значение внутри Constant

4. EF Core получает:
   src => newHolder.lang == "ru" ? src.NameRu : src.NameUz
   ↑ MemberAccess(Constant(holder), "lang") — стандартный closure capture!

5. EF Core транслирует:
   SELECT CASE WHEN @__lang_0 = 'ru' THEN "p"."NameRu" ELSE "p"."NameUz" END
   FROM "Products" AS "p"
   -- @__lang_0 = 'ru' — SQL-параметр, не инлайновая константа!

6. При следующем вызове с lang="uz":
   - Создаётся новый holder с lang="uz"
   - Форма дерева та же → EF Core ПЕРЕИСПОЛЬЗУЕТ план запроса
   - Только значение параметра меняется: @__lang_0 = 'uz'
```

---

## Конвенции автоматического маппинга

При вызове `CreateMap<S, D>()` без указания `ForMember` для каждого свойства `D`, `ProjectionCompiler` автоматически пытается найти соответствие.

### DefaultNameConvention

Ищет свойство в `TSource` с таким же именем (case-insensitive):

```csharp
Source.Name → Dest.Name
Source.Id   → Dest.Id
Source.Price → Dest.Price
```

Поддерживает nullable-обёртки: `int` -> `int?`.

### FlatteningConvention

Рекурсивно разворачивает вложенные объекты по конкатенации имён:

```csharp
Source.Address.Street  → Dest.AddressStreet
Source.Address.City    → Dest.AddressCity
Source.Address.ZipCode → Dest.AddressZipCode
```

Алгоритм:
1. Берёт имя свойства назначения (например, `AddressCity`)
2. Ищет свойство-префикс в `TSource` (`Address`)
3. Рекурсивно ищет остаток (`City`) в типе найденного свойства
4. Максимальная глубина: 5 уровней (защита от stack overflow)
5. Сортирует свойства по длине имени (longest match first)

### Приоритет

Явный `ForMember` всегда перекрывает конвенции:

```csharp
CreateMap<Source, Dest>()
    .ForMember(d => d.Name, o => o.MapFrom(s => s.Title)); // Title, не Name
```

---

## Fluent API

### MappingProfile

```csharp
public abstract class MappingProfile
{
    // Создать маппинг между типами
    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>();

    // Объявить runtime-параметр для параметризованных проекций
    protected ParameterSlot<T> DeclareParameter<T>(string name);
}
```

### ITypeMappingExpression&lt;TSource, TDest&gt;

```csharp
// Настроить маппинг конкретного свойства
.ForMember(d => d.Prop, options => ...)

// Игнорировать свойство (не маппить)
.Ignore(d => d.Prop)

// Кастомный конструктор
.ConstructUsing(src => new Dest { ... })

// Создать обратный маппинг (Dest → Source)
.ReverseMap()
```

### IMemberOptions&lt;TSource, TDest, TMember&gt;

```csharp
// Маппинг из выражения
options.MapFrom(src => src.SomeProp)

// Маппинг с runtime-параметром
options.MapFrom(langSlot, (src, lang) => lang == "ru" ? src.NameRu : src.NameUz)

// Игнорировать
options.Ignore()
```

---

## Интеграция с DI

```csharp
// Автоматическое сканирование сборки — находит все наследники MappingProfile
builder.Services.AddMapping(typeof(Program).Assembly);
```

С ручной конфигурацией:

```csharp
builder.Services.AddMapping(
    cfg => cfg.AddProfile<MyCustomProfile>(),
    typeof(SomeProfile).Assembly);
```

Регистрирует как **Singleton**:
- `MapperConfiguration` — frozen конфигурация
- `IMapper` — stateless маппер
- `IProjectionProvider` — провайдер проекций

Все маппинги валидируются при регистрации. При ошибках — `MappingValidationException` со списком всех проблем.

---

## Пример: Web API с локализацией

Проект `samples/MyAutoMapper.WebApiSample` — рабочий пример ASP.NET Core Web API с EF Core SQLite и параметризованной локализацией.

### Профиль с параметризацией

```csharp
public class ProductMappingProfile : MappingProfile
{
    public ProductMappingProfile()
    {
        var lang = DeclareParameter<string>("lang");

        CreateMap<Product, ProductViewModel>()
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.NameUz
                          : l == "lt" ? src.NameLt
                          : src.NameRu))
            .ForMember(d => d.LocalizedDescription, o => o.MapFrom(lang,
                (src, l) => l == "uz" ? src.DescriptionUz
                          : l == "lt" ? src.DescriptionLt
                          : src.DescriptionRu));
    }
}
```

### Контроллер

```csharp
[HttpGet]
public IActionResult GetAll([FromQuery] string lang = "ru")
{
    var products = _db.Products
        .ProjectTo<Product, ProductViewModel>(_projections,
            p => p.Set("lang", lang))
        .ToList();

    return Ok(products);
}
```

### Program.cs

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=sample.db"));

builder.Services.AddMapping(typeof(Program).Assembly);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
```

### Результат

`GET /api/products?lang=ru`:
```json
[
  {"id": 1, "localizedName": "iPhone 16 Pro", "localizedDescription": "Новейший смартфон Apple", "price": 12990000.0},
  {"id": 2, "localizedName": "Samsung Galaxy S25", "localizedDescription": "Флагманский смартфон Samsung", "price": 10490000.0}
]
```

`GET /api/products?lang=uz`:
```json
[
  {"id": 1, "localizedName": "iPhone 16 Pro", "localizedDescription": "Eng yangi Apple smartfoni", "price": 12990000.0}
]
```

SQL, генерируемый EF Core:
```sql
SELECT "p"."NameRu" AS "LocalizedName", "p"."DescriptionRu" AS "LocalizedDescription",
       "p"."Id", "p"."Price"
FROM "Products" AS "p"
```

### Запуск примера

```bash
cd samples/MyAutoMapper.WebApiSample
dotnet run
# Swagger UI: http://localhost:5000/swagger
```

### API-эндпоинты

| Эндпоинт | Описание |
|---|---|
| `GET /api/products?lang=ru` | Все продукты с локализацией |
| `GET /api/products/{id}?lang=ru` | Один продукт по Id |
| `GET /api/products/by-category/{id}?lang=uz` | Продукты по категории |
| `GET /api/categories/tree?lang=ru` | Дерево категорий с иерархией |
| `GET /api/categories/flat?lang=lt` | Плоский список категорий |

---

## Тестирование

### Unit-тесты

```bash
dotnet test tests/MyAutoMapper.UnitTests
```

| Файл тестов | Что проверяет |
|---|---|
| `MappingProfileTests` | Создание профилей, накопление TypeMaps |
| `MapperConfigurationTests` | Компиляция, кэширование, обработка ошибок |
| `MapperTests` | In-memory маппинг, null source |
| `ProjectionProviderTests` | Проекции без параметров и с параметрами |
| `ConventionMappingTests` | Конвенции, flattening, ReverseMap |
| `ConfigurationValidatorTests` | Валидация типов, обнаружение ошибок |

### Интеграционные тесты

```bash
dotnet test tests/MyAutoMapper.IntegrationTests
```

| Файл тестов | Что проверяет |
|---|---|
| `ProjectToTests` | ProjectTo генерирует корректный SQL |
| `ParameterizedProjectionTests` | Параметризация: lang=en, lang=fr, unknown |
| `ServiceCollectionTests` | DI-регистрация, singleton lifetimes |

---

## Бенчмарки

Сравнение **MyAutoMapper** vs **AutoMapper** (16.1.1) vs **Mapster** (10.0.3) vs ручной маппинг.

### Доступные бенчмарки

| Бенчмарк | Что измеряет |
|---|---|
| `SimpleMappingBenchmark` | Плоский маппинг, 3 свойства |
| `ComplexMappingBenchmark` | Плоский маппинг, 10 свойств |
| `FlatteningBenchmark` | Разворачивание вложенных объектов |
| `ConfigurationBenchmark` | Стоимость конфигурации (Build/Compile) |

### Запуск бенчмарков

```bash
cd tests/MyAutoMapper.Benchmarks

# Все бенчмарки (5-15 мин)
dotnet run -c Release -- --filter *

# Конкретный бенчмарк
dotnet run -c Release -- --filter *SimpleMappingBenchmark*
dotnet run -c Release -- --filter *FlatteningBenchmark*

# Быстрый прогон (менее точный, 1-3 мин)
dotnet run -c Release -- --filter * --job short

# Список всех бенчмарков
dotnet run -c Release -- --list flat

# Экспорт результатов
dotnet run -c Release -- --filter * --exporters markdown
dotnet run -c Release -- --filter * --exporters json
dotnet run -c Release -- --filter * --exporters html
```

Результаты сохраняются в `BenchmarkDotNet.Artifacts/results/`.

> **Важно**: всегда запускайте с `-c Release`. Debug-сборки дают некорректные результаты.

---

## Сборка и запуск

```bash
# Сборка всего solution
dotnet build

# Все тесты
dotnet test

# Запуск Web API примера
cd samples/MyAutoMapper.WebApiSample
dotnet run

# Бенчмарки
cd tests/MyAutoMapper.Benchmarks
dotnet run -c Release -- --filter *
```

---

## Использованные технологии

| Технология | Где используется |
|---|---|
| **.NET 10 / C# 14** | Вся кодовая база (`LangVersion preview`, `.slnx` формат) |
| **Expression Trees** (`System.Linq.Expressions`) | Генерация `Expression<Func<S,D>>` для EF Core и `Func<S,D>` делегатов |
| **ExpressionVisitor** | `ParameterReplacer`, `ClosureValueInjector` — обход и трансформация деревьев |
| **System.Reflection.Emit** | `ClosureHolderFactory` — динамическая генерация POCO-типов |
| **ConcurrentDictionary** | Кэширование `TypeMap`, `HolderTypeInfo` |
| **Central Package Management** | `Directory.Packages.props` — единое управление версиями NuGet |
| **Microsoft.Extensions.DI** | `AddMapping()`, `IServiceCollection`, Singleton |
| **EF Core + SQLite** | Интеграционные тесты, пример Web API |
| **BenchmarkDotNet** | Бенчмарки vs AutoMapper, Mapster |
| **xUnit + FluentAssertions** | Unit- и интеграционные тесты |
| **Swashbuckle** | Swagger UI в примере Web API |

## Лицензия

MIT
