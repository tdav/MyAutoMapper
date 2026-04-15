# MyAutoMapper

> **[English version (README.md)](README.md)**

Легковесная высокопроизводительная библиотека маппинга объектов для **.NET 10** с первоклассной поддержкой **параметризованных EF Core проекций**.

Единственная внешняя зависимость — `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Быстрый старт

```csharp
// 1. Регистрация в DI (Program.cs)
builder.Services.AddMapping(typeof(Program).Assembly);

// 2. Использование в любом классе — инъекция не нужна!
var products = await _db.Products
    .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
    .ToListAsync();

// Или без параметров:
var categories = await _db.Categories
    .ProjectTo<CategoryDto>()
    .ToListAsync();
```

---

## Возможности

| Возможность | Описание |
|---|---|
| **Expression-проекции** | Генерирует `Expression<Func<TSource, TDest>>` для EF Core `IQueryable` — в SQL попадают только нужные столбцы |
| **Параметризованные проекции** | `ParameterSlot<T>` использует closure-паттерн, который EF Core транслирует в SQL-параметры (`@__param_0`), сохраняя кэш планов запросов |
| **Проекция вложенных коллекций** | `IEnumerable<TSrc> → IEnumerable<TDest>` проецируется автоматически через `.Select(new TDest {...})`; самоссылочные иерархии ограничиваются `MaxDepth(n)` |
| **In-memory маппинг** | Скомпилированные `Func<TSource, TDest>` делегаты для быстрого маппинга объект-в-объект |
| **Конвенции** | Автоматический маппинг по совпадению имён + flattening вложенных объектов (`Address.City` -> `AddressCity`) |
| **Fluent API** | `CreateMap<S, D>().ForMember(...).Ignore(...).ConstructUsing(...).ReverseMap()` |
| **Eager-валидация** | Все маппинги проверяются при старте; ошибки выбрасываются сразу с полным списком проблем |
| **DI-интеграция** | `services.AddMapping()` со сканированием сборок и синглтон-регистрацией |

## Требования

- .NET 10 SDK (10.0.201+)
- C# 14 (`LangVersion preview`)

---

## Fluent API

### MappingProfile

```csharp
public class ProductProfile : MappingProfile
{
    public ProductProfile()
    {
        // Простой маппинг с конвенциями
        CreateMap<Product, ProductDto>();

        // Кастомный маппинг свойств
        CreateMap<Product, ProductDetailDto>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Title))
            .Ignore(d => d.InternalCode)
            .ConstructUsing(src => new ProductDetailDto { Source = "db" })
            .ReverseMap();

        // Параметризованная проекция
        var lang = DeclareParameter<string>("lang");
        CreateMap<Product, ProductViewModel>()
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "ru" ? src.NameRu : src.NameUz));
    }
}
```

### ProjectTo API

Три уровня, от простого к явному:

```csharp
// 1. Один generic-параметр (рекомендуется) — TSource определяется в runtime
source.ProjectTo<ProductDto>();
source.ProjectTo<ProductDto>(p => p.Set("lang", "ru"));

// 2. Два generic-параметра — TSource явный
source.ProjectTo<Product, ProductDto>();
source.ProjectTo<Product, ProductDto>(p => p.Set("lang", "ru"));

// 3. Явный провайдер — полный контроль, удобно для тестов
source.ProjectTo<Product, ProductDto>(projectionProvider);
source.ProjectTo<Product, ProductDto>(projectionProvider, p => p.Set("lang", "ru"));
```

### In-Memory маппинг

```csharp
var mapper = serviceProvider.GetRequiredService<IMapper>();
var dto = mapper.Map<Product, ProductDto>(product);
```

---

## Конвенции автоматического маппинга

При вызове `CreateMap<S, D>()` без `ForMember` компилятор автоматически подбирает свойства.

**Совпадение имён** (case-insensitive):
```
Source.Name  → Dest.Name
Source.Price → Dest.Price
```

**Flattening вложенных объектов**:
```
Source.Address.City    → Dest.AddressCity
Source.Address.ZipCode → Dest.AddressZipCode
```

**Коллекции с одинаковыми именами**:
```
Source.Children (List<Category>)   → Dest.Children (List<CategoryViewModel>)
Source.Products (List<Product>)    → Dest.Products (List<ProductDto>)
```
Если для типов элементов зарегистрирован `CreateMap<Category, CategoryViewModel>()`,
компилятор эмитит `src.Children.Select(c => new CategoryViewModel { ... }).ToList()`
рекурсивно — дополнительный `ForMember` не нужен.

Явный `ForMember` всегда перекрывает конвенции.

---

## Проекция вложенных коллекций

Коллекционные свойства с зарегистрированным маппингом элементов проецируются
рекурсивно одним EF Core запросом. Без дополнительной конфигурации и без ручных
`.Select(...)` в контроллере.

```csharp
public class CategoryProfile : MappingProfile
{
    public CategoryProfile()
    {
        // Самоссылка: CategoryViewModel содержит List<CategoryViewModel> Children.
        // MaxDepth(n) ограничивает глубину генерируемого дерева —
        // иначе рекурсия была бы бесконечной.
        CreateMap<Category, CategoryViewModel>().MaxDepth(5);
    }
}

// Контроллер — один запрос, всё дерево:
var tree = _db.Categories
    .Where(c => c.ParentId == null)
    .ProjectTo<CategoryViewModel>()
    .ToList();
```

Генерируемый SQL обходит иерархию через `LEFT JOIN LATERAL` (или несколько
подзапросов — зависит от EF Core провайдера). Никакого N+1.

**Правила:**

- Поддерживаемые типы коллекций: `IEnumerable<T>`, `ICollection<T>`, `IList<T>`,
  `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, массивы (`T[]`) и `List<T>`.
- Маппинг между типами элементов должен быть зарегистрирован через
  `CreateMap<TSrc, TDest>()`.
- Для самоссылочных маппингов `MaxDepth(n)` обязателен — без него валидация
  конфигурации бросит исключение. `n` — максимальное число повторов одной и той
  же пары типов на одной рекурсивной ветке.
- Для не-рекурсивных вложенных коллекций (например `Category.Products`)
  `MaxDepth` опционален.

---

## Интеграция с DI

```csharp
// Автоматическое сканирование сборки — находит все наследники MappingProfile
builder.Services.AddMapping(typeof(Program).Assembly);

// С ручной конфигурацией
builder.Services.AddMapping(
    cfg => cfg.AddProfile<MyCustomProfile>(),
    typeof(SomeProfile).Assembly);
```

Регистрирует как **Singleton**:
- `MapperConfiguration` — frozen конфигурация
- `IMapper` — stateless маппер
- `IProjectionProvider` — провайдер проекций + статический `ProjectionProviderAccessor`

Все маппинги валидируются при регистрации. При ошибках — `MappingValidationException` со списком всех проблем.

---

## Как работают параметризованные проекции

Это ключевая инновация библиотеки. Обычные подходы (string interpolation, замена констант) ломают кэш планов запросов EF Core. MyAutoMapper использует closure-паттерн — тот же, что C# компилятор генерирует для замыканий.

### Этап конфигурации

```
1. Пользователь объявляет:
   var lang = DeclareParameter<string>("lang");
   CreateMap<Product, ProductViewModel>()
       .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
           (src, l) => l == "ru" ? src.NameRu : src.NameUz))

2. Библиотека генерирует динамический closure holder:
   class ClosureHolder_1 { public string lang { get; set; } }

3. Компилятор строит выражение с closure-паттерном:
   src => holderInstance.lang == "ru" ? src.NameRu : src.NameUz
```

### Этап запроса

```
1. Пользователь вызывает:
   _db.Products.ProjectTo<ProductViewModel>(p => p.Set("lang", "ru"))

2. Библиотека создаёт новый holder с lang="ru", подменяет его в дереве выражений.
   Форма дерева ИДЕНТИЧНА — изменилось только значение.

3. EF Core транслирует:
   SELECT CASE WHEN @__lang_0 = 'ru' THEN "p"."NameRu" ELSE "p"."NameUz" END
   FROM "Products" AS "p"
   -- @__lang_0 — SQL-параметр, не инлайновая константа!

4. При следующем вызове с lang="uz" → EF Core ПЕРЕИСПОЛЬЗУЕТ план запроса.
   Меняется только значение параметра: @__lang_0 = 'uz'
```

---

## Пример: Web API с локализацией

Проект `samples/MyAutoMapper.WebApiSample` — рабочий пример ASP.NET Core Web API с EF Core SQLite и параметризованной локализацией.

### Профиль

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
public async Task<IActionResult> GetAll([FromQuery] string lang = "ru")
{
    var products = await _db.Products
        .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
        .ToListAsync();

    return Ok(products);
}
```

### Результат

`GET /api/products?lang=ru`:
```json
[
  {"id": 1, "localizedName": "iPhone 16 Pro", "localizedDescription": "Новейший смартфон Apple", "price": 12990000.0},
  {"id": 2, "localizedName": "Samsung Galaxy S25", "localizedDescription": "Флагманский смартфон Samsung", "price": 10490000.0}
]
```

SQL, генерируемый EF Core:
```sql
SELECT CASE WHEN @__lang_0 = 'ru' THEN "p"."NameRu" ELSE "p"."NameUz" END AS "LocalizedName",
       "p"."Id", "p"."Price"
FROM "Products" AS "p"
```

### Дерево категорий (вложенная проекция)

```csharp
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
                          : src.NameRu));
        // Children проецируется автоматически по конвенции (совпадение имени с Category.Children).
    }
}

// Контроллер:
var tree = _db.Categories
    .Where(c => c.ParentId == null)
    .ProjectTo<CategoryViewModel>(p => p.Set("lang", lang))
    .ToList();
```

`GET /api/categories/tree?lang=ru` возвращает всю иерархию до глубины 5 одним
SQL-запросом.

> **Замечание:** holder-ы параметров пока не шарятся между уровнями рекурсии —
> значение `lang` применяется только к корневому уровню, вложенные children
> откатываются на `NameRu`. Ограничение зафиксировано TODO в `ProjectionCompiler`.

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

```bash
# Unit-тесты
dotnet test tests/MyAutoMapper.UnitTests

# Интеграционные тесты (EF Core SQLite)
dotnet test tests/MyAutoMapper.IntegrationTests

# Все тесты
dotnet test
```

## Бенчмарки

Сравнение **MyAutoMapper** vs **AutoMapper** (16.1.1) vs **Mapster** (10.0.3) vs ручной маппинг.

```bash
cd tests/MyAutoMapper.Benchmarks

# Все бенчмарки (5-15 мин)
dotnet run -c Release -- --filter *

# Конкретный бенчмарк
dotnet run -c Release -- --filter *SimpleMappingBenchmark*

# Быстрый прогон (менее точный, 1-3 мин)
dotnet run -c Release -- --filter * --job short
```

> **Важно**: всегда запускайте с `-c Release`. Debug-сборки дают некорректные результаты.

---

## Сборка и запуск

```bash
# Сборка
dotnet build

# Тесты
dotnet test

# Запуск Web API примера
cd samples/MyAutoMapper.WebApiSample
dotnet run

# Упаковка для NuGet
dotnet pack -c Release
```

## Лицензия

MIT
