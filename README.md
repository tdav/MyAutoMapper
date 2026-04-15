# MyAutoMapper

> **[Русская версия (README-ru.md)](README-ru.md)**

Lightweight, high-performance object mapping library for **.NET 10** with first-class support for **parameterized EF Core projections**.

Only external dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Quick Start

```csharp
// 1. Register in DI (Program.cs)
builder.Services.AddMapping(typeof(Program).Assembly);

// 2. Use anywhere — no injection needed!
var products = await _db.Products
    .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
    .ToListAsync();

// Or without parameters:
var categories = await _db.Categories
    .ProjectTo<CategoryDto>()
    .ToListAsync();
```

---

## Features

| Feature | Description |
|---|---|
| **Expression projections** | Generates `Expression<Func<TSource, TDest>>` for EF Core `IQueryable` — only mapped columns appear in SQL |
| **Parameterized projections** | `ParameterSlot<T>` uses a closure pattern that EF Core translates to native SQL parameters (`@__param_0`), preserving the query plan cache |
| **Nested collection projection** | `IEnumerable<TSrc> → IEnumerable<TDest>` projected automatically via `.Select(new TDest {...})`; self-referential hierarchies bounded by `MaxDepth(n)` |
| **In-memory mapping** | Compiled `Func<TSource, TDest>` delegates for fast object-to-object mapping |
| **Conventions** | Auto-maps by matching property names + flattens nested objects (`Address.City` -> `AddressCity`) |
| **Fluent API** | `CreateMap<S, D>().ForMember(...).Ignore(...).ConstructUsing(...).ReverseMap()` |
| **Eager validation** | All mappings validated at startup; throws immediately with the full list of issues |
| **DI integration** | `services.AddMapping()` with assembly scanning and singleton registration |

## Requirements

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
        // Simple mapping with conventions
        CreateMap<Product, ProductDto>();

        // Custom member mapping
        CreateMap<Product, ProductDetailDto>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Title))
            .Ignore(d => d.InternalCode)
            .ConstructUsing(src => new ProductDetailDto { Source = "db" })
            .ReverseMap();

        // Parameterized projection
        var lang = DeclareParameter<string>("lang");
        CreateMap<Product, ProductViewModel>()
            .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
                (src, l) => l == "ru" ? src.NameRu : src.NameUz));
    }
}
```

### ProjectTo API

Three levels, from simplest to most explicit:

```csharp
// 1. Single generic parameter (recommended) — TSource inferred at runtime
source.ProjectTo<ProductDto>();
source.ProjectTo<ProductDto>(p => p.Set("lang", "ru"));

// 2. Two generic parameters — TSource explicit
source.ProjectTo<Product, ProductDto>();
source.ProjectTo<Product, ProductDto>(p => p.Set("lang", "ru"));

// 3. Explicit provider — full control, useful for testing
source.ProjectTo<Product, ProductDto>(projectionProvider);
source.ProjectTo<Product, ProductDto>(projectionProvider, p => p.Set("lang", "ru"));
```

### In-Memory Mapping

```csharp
var mapper = serviceProvider.GetRequiredService<IMapper>();
var dto = mapper.Map<Product, ProductDto>(product);
```

---

## Convention-Based Auto-Mapping

When `CreateMap<S, D>()` is called without `ForMember`, the compiler automatically matches properties.

**Same name** (case-insensitive):
```
Source.Name  → Dest.Name
Source.Price → Dest.Price
```

**Nested object flattening**:
```
Source.Address.City    → Dest.AddressCity
Source.Address.ZipCode → Dest.AddressZipCode
```

**Collections with the same name**:
```
Source.Children (List<Category>)   → Dest.Children (List<CategoryViewModel>)
Source.Products (List<Product>)    → Dest.Products (List<ProductDto>)
```
If `CreateMap<Category, CategoryViewModel>()` exists, the compiler emits
`src.Children.Select(c => new CategoryViewModel { ... }).ToList()` recursively —
no extra `ForMember` needed.

Explicit `ForMember` always overrides conventions.

---

## Nested Collection Projection

Collection properties with mapped element types are projected recursively into a
single EF Core query. No extra configuration, no manual `.Select(...)` in the
controller.

```csharp
public class CategoryProfile : MappingProfile
{
    public CategoryProfile()
    {
        // Self-referential: CategoryViewModel has List<CategoryViewModel> Children.
        // MaxDepth(n) bounds the generated tree — otherwise recursion would be infinite.
        CreateMap<Category, CategoryViewModel>().MaxDepth(5);
    }
}

// Controller — one query, full tree:
var tree = _db.Categories
    .Where(c => c.ParentId == null)
    .ProjectTo<CategoryViewModel>()
    .ToList();
```

The generated SQL traverses the hierarchy via `LEFT JOIN LATERAL` (or multiple
subqueries, depending on the EF Core provider) — there is no N+1.

**Rules:**

- Collection types supported: `IEnumerable<T>`, `ICollection<T>`, `IList<T>`,
  `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, arrays (`T[]`) and concrete `List<T>`.
- Mapping between element types must be registered with `CreateMap<TSrc, TDest>()`.
- For self-references, `MaxDepth(n)` is mandatory — without it configuration
  validation throws. `n` is the maximum number of times the same map may appear
  on a single recursive branch.
- For non-recursive nested collections (e.g. `Category.Products`), `MaxDepth` is
  optional.

---

## DI Integration

```csharp
// Auto-scan assembly — finds all MappingProfile subclasses
builder.Services.AddMapping(typeof(Program).Assembly);

// With manual configuration
builder.Services.AddMapping(
    cfg => cfg.AddProfile<MyCustomProfile>(),
    typeof(SomeProfile).Assembly);
```

Registers as **Singleton**:
- `MapperConfiguration` — frozen configuration
- `IMapper` — stateless mapper
- `IProjectionProvider` — projection provider + static `ProjectionProviderAccessor`

All mappings validated at registration. On errors: `MappingValidationException` with the full list of issues.

---

## How Parameterized Projections Work

This is the library's key innovation. Common approaches (string interpolation, constant replacement) break EF Core's query plan cache. MyAutoMapper uses the closure pattern — the same one the C# compiler generates for closures.

### Configuration Phase

```
1. User declares:
   var lang = DeclareParameter<string>("lang");
   CreateMap<Product, ProductViewModel>()
       .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
           (src, l) => l == "ru" ? src.NameRu : src.NameUz))

2. Library generates a dynamic closure holder:
   class ClosureHolder_1 { public string lang { get; set; } }

3. Compiler builds an expression with closure pattern:
   src => holderInstance.lang == "ru" ? src.NameRu : src.NameUz
```

### Query Phase

```
1. User calls:
   _db.Products.ProjectTo<ProductViewModel>(p => p.Set("lang", "ru"))

2. Library creates a new holder with lang="ru", swaps it in the expression tree.
   Tree shape stays IDENTICAL — only the value changes.

3. EF Core translates:
   SELECT CASE WHEN @__lang_0 = 'ru' THEN "p"."NameRu" ELSE "p"."NameUz" END
   FROM "Products" AS "p"
   -- @__lang_0 is a SQL parameter, not an inlined constant!

4. Next call with lang="uz" → EF Core REUSES the query plan.
   Only the parameter value changes: @__lang_0 = 'uz'
```

---

## Example: Web API with Localization

The project `samples/MyAutoMapper.WebApiSample` is a working ASP.NET Core Web API with EF Core SQLite and parameterized localization.

### Profile

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

### Controller

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

### Output

`GET /api/products?lang=ru`:
```json
[
  {"id": 1, "localizedName": "iPhone 16 Pro", "localizedDescription": "Newest Apple smartphone", "price": 12990000.0},
  {"id": 2, "localizedName": "Samsung Galaxy S25", "localizedDescription": "Flagship Samsung smartphone", "price": 10490000.0}
]
```

SQL generated by EF Core:
```sql
SELECT CASE WHEN @__lang_0 = 'ru' THEN "p"."NameRu" ELSE "p"."NameUz" END AS "LocalizedName",
       "p"."Id", "p"."Price"
FROM "Products" AS "p"
```

### Category Tree (nested projection)

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
        // Children is projected automatically via convention (same name as Category.Children).
    }
}

// Controller:
var tree = _db.Categories
    .Where(c => c.ParentId == null)
    .ProjectTo<CategoryViewModel>(p => p.Set("lang", lang))
    .ToList();
```

`GET /api/categories/tree?lang=ru` returns the full hierarchy up to depth 5 as
a single SQL query.

> **Note:** parameter holders are not shared across recursive levels yet — the
> `lang` value currently applies to the root level only; nested children fall
> back to `NameRu`. Tracked as a TODO in `ProjectionCompiler`.

### Running the Example

```bash
cd samples/MyAutoMapper.WebApiSample
dotnet run
# Swagger UI: http://localhost:5000/swagger
```

### API Endpoints

| Endpoint | Description |
|---|---|
| `GET /api/products?lang=ru` | All products with localization |
| `GET /api/products/{id}?lang=ru` | Single product by Id |
| `GET /api/products/by-category/{id}?lang=uz` | Products by category |
| `GET /api/categories/tree?lang=ru` | Category tree with hierarchy |
| `GET /api/categories/flat?lang=lt` | Flat category list |

---

## Testing

```bash
# Unit tests
dotnet test tests/MyAutoMapper.UnitTests

# Integration tests (EF Core SQLite)
dotnet test tests/MyAutoMapper.IntegrationTests

# All tests
dotnet test
```

## Benchmarks

Compares **MyAutoMapper** vs **AutoMapper** (16.1.1) vs **Mapster** (10.0.3) vs manual mapping.

```bash
cd tests/MyAutoMapper.Benchmarks

# All benchmarks (5-15 min)
dotnet run -c Release -- --filter *

# Specific benchmark
dotnet run -c Release -- --filter *SimpleMappingBenchmark*

# Quick run (less accurate, 1-3 min)
dotnet run -c Release -- --filter * --job short


BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
Unknown processor
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.526.15411), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.5 (10.0.526.15411), X64 RyuJIT AVX2

Categories=Simple

| Method       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Manual       |  3.491 ns | 0.0136 ns | 0.0113 ns |  1.00 |    0.00 | 0.0031 |      48 B |        1.00 |
| Mapster      |  8.687 ns | 0.0659 ns | 0.0584 ns |  2.49 |    0.02 | 0.0031 |      48 B |        1.00 |
| SmAutoMapper | 14.566 ns | 0.2508 ns | 0.2224 ns |  4.17 |    0.06 | 0.0030 |      48 B |        1.00 |
| AutoMapper   | 25.699 ns | 0.0652 ns | 0.0578 ns |  7.36 |    0.03 | 0.0030 |      48 B |        1.00 |
```

> **Important**: always run with `-c Release`. Debug builds produce inaccurate results.

---

## Building and Running

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run Web API example
cd samples/MyAutoMapper.WebApiSample
dotnet run

# Pack for NuGet
dotnet pack -c Release

dotnet pack src/MyAutoMapper/SmAutoMapper.csproj -c Release -o ./nupkg
```

## License

MIT
