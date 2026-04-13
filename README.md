# MyAutoMapper

> **[Русская версия (README-ru.md)](README-ru.md)**

Lightweight, high-performance object mapping library for **.NET 10** with first-class support for **parameterized EF Core projections**.

Only external dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Project Structure](#project-structure)
- [Architecture](#architecture)
  - [Configuration Layer](#1-configuration-layer)
  - [Compilation Layer](#2-compilation-layer)
  - [Parameters Layer](#3-parameters-layer)
  - [Runtime Layer](#4-runtime-layer)
  - [Extensions](#5-extensions)
  - [Validation](#6-validation)
- [How Mapping Works — Step by Step](#how-mapping-works--step-by-step)
- [How Parameterized Projections Work — Step by Step](#how-parameterized-projections-work--step-by-step)
- [Convention-Based Auto-Mapping](#convention-based-auto-mapping)
- [Fluent API](#fluent-api)
- [DI Integration](#di-integration)
- [Example: Web API with Localization](#example-web-api-with-localization)
- [Testing](#testing)
- [Benchmarks](#benchmarks)
- [Building and Running](#building-and-running)

---

## Features

| Feature | Description |
|---|---|
| **Expression projections** | Generates `Expression<Func<TSource, TDest>>` for EF Core `IQueryable` — only mapped columns appear in SQL |
| **Parameterized projections** | `ParameterSlot<T>` uses a closure pattern that EF Core translates to native SQL parameters (`@__param_0`), preserving the query plan cache |
| **In-memory mapping** | Compiled `Func<TSource, TDest>` delegates for fast object-to-object mapping |
| **Conventions** | Auto-maps by matching property names + flattens nested objects (`Address.City` -> `AddressCity`) |
| **Fluent API** | `CreateMap<S, D>().ForMember(...).Ignore(...).ConstructUsing(...).ReverseMap()` |
| **Eager validation** | All mappings validated at startup; throws immediately with the full list of issues |
| **DI integration** | `services.AddMapping()` with assembly scanning and singleton registration |

## Quick Start

```csharp
// 1. Register in DI (Program.cs)
builder.Services.AddMapping(typeof(Program).Assembly);

// 2. Use in any class — no injection needed!
var products = await _db.Products
    .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
    .ToListAsync();

// Or without parameters:
var categories = await _db.Categories
    .ProjectTo<CategoryDto>()
    .ToListAsync();
```

No need to inject `IProjectionProvider` — it resolves automatically via a static accessor configured at startup.

---

## Requirements

- .NET 10 SDK (10.0.201+)
- C# 14 (`LangVersion preview`)

## Project Structure

```
MyAutoMapper/
├── MyAutoMapper.slnx                      # .NET 10 XML solution
├── Directory.Build.props                  # net10.0, C# 14, Nullable, TreatWarningsAsErrors
├── Directory.Packages.props               # Central Package Management
│
├── src/MyAutoMapper/                      # Core library
│   ├── Configuration/                     # Fluent API, profiles, builders
│   │   ├── MappingProfile.cs              # Abstract base class for profiles
│   │   ├── ITypeMappingExpression.cs      # Fluent interface for CreateMap
│   │   ├── IMemberOptions.cs              # Fluent interface for ForMember
│   │   ├── TypeMapBuilder.cs              # Fluent configuration implementation
│   │   ├── MemberMapBuilder.cs            # Per-property configuration
│   │   ├── PropertyMap.cs                 # Single property mapping metadata
│   │   ├── MappingConfigurationBuilder.cs # Accumulates profiles, Build()
│   │   └── ITypeMapConfiguration.cs       # Non-generic metadata interface
│   │
│   ├── Compilation/                       # Expression tree engine
│   │   ├── ProjectionCompiler.cs          # Builds Expression<Func<S,D>>
│   │   ├── InMemoryCompiler.cs            # Compiles to Func<S,D> delegate
│   │   ├── ParameterReplacer.cs           # ExpressionVisitor: parameter substitution
│   │   ├── ClosureValueInjector.cs        # ExpressionVisitor: closure holder swap
│   │   ├── MapperConfiguration.cs         # Frozen singleton, ConcurrentDictionary
│   │   ├── TypeMap.cs                     # Immutable compiled mapping container
│   │   ├── TypePair.cs                    # readonly record struct — cache key
│   │   └── Conventions/                   # Auto-mapping conventions
│   │       ├── INameConvention.cs         # Convention interface
│   │       ├── DefaultNameConvention.cs   # Same-name match (case-insensitive)
│   │       └── FlatteningConvention.cs    # Nested object flattening
│   │
│   ├── Parameters/                        # Projection parameterization
│   │   ├── ParameterSlot.cs               # Parameter declaration in profiles
│   │   ├── IParameterSlot.cs              # Non-generic slot interface
│   │   ├── IParameterBinder.cs            # Value binding interface
│   │   ├── ParameterBinder.cs             # Dictionary-based implementation
│   │   └── ClosureHolderFactory.cs        # Dynamic POCO generation via Reflection.Emit
│   │
│   ├── Runtime/                           # Runtime mapping
│   │   ├── IMapper.cs                     # In-memory mapper interface
│   │   ├── Mapper.cs                      # Compiled delegate invocation
│   │   ├── IProjectionProvider.cs         # Projection provider interface
│   │   ├── ProjectionProvider.cs          # Expression delivery + parameter injection
│   │   ├── ProjectionProviderAccessor.cs  # Static singleton for implicit resolution
│   │   └── MappingContext.cs              # Key/value context
│   │
│   ├── Extensions/                        # Extension methods
│   │   ├── ServiceCollectionExtensions.cs # AddMapping() for DI
│   │   └── QueryableExtensions.cs         # ProjectTo<TDest>() for IQueryable
│   │
│   └── Validation/                        # Configuration validation
│       ├── ConfigurationValidator.cs      # Type compatibility checks
│       └── MappingValidationException.cs  # Exception with error list
│
├── samples/
│   └── MyAutoMapper.WebApiSample/         # ASP.NET Core Web API example
│
├── tests/
│   ├── MyAutoMapper.UnitTests/            # Unit tests (xUnit)
│   ├── MyAutoMapper.IntegrationTests/     # EF Core SQLite integration tests
│   └── MyAutoMapper.Benchmarks/           # BenchmarkDotNet (vs AutoMapper, Mapster)
```

---

## Architecture

The library is organized into 6 layers, each with a single clear responsibility.

### 1. Configuration Layer

**Purpose**: collect mapping metadata through a Fluent API.

#### MappingProfile

Abstract base class. Users inherit from it and describe mappings in the constructor:

```csharp
public abstract class MappingProfile
{
    internal List<ITypeMapConfiguration> TypeMaps { get; } = [];

    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>();
    protected ParameterSlot<T> DeclareParameter<T>(string name);
}
```

- `CreateMap<S, D>()` — creates a `TypeMapBuilder<S, D>`, adds it to `TypeMaps`, and returns the fluent interface.
- `DeclareParameter<T>(name)` — creates a `ParameterSlot<T>` for use in parameterized mappings.

#### TypeMapBuilder&lt;TSource, TDest&gt;

Implements both `ITypeMappingExpression<S, D>` (fluent API) and `ITypeMapConfiguration` (for the compiler). Accumulates a list of `PropertyMap` entries internally:

```csharp
internal sealed class TypeMapBuilder<TSource, TDest>
    : ITypeMappingExpression<TSource, TDest>, ITypeMapConfiguration
{
    private readonly List<PropertyMap> _propertyMaps = [];
    private LambdaExpression? _customConstructor;
    private TypeMapBuilder<TDest, TSource>? _reverseMap;
}
```

**PropertyInfo extraction** from lambda expressions: the `ExtractPropertyInfo` method handles `UnaryExpression` (for value types wrapped in `Convert`) and `MemberExpression`.

#### MemberMapBuilder&lt;TSource, TDest, TMember&gt;

Implements `IMemberOptions<S, D, M>`. Two `MapFrom` variants:

```csharp
// Standard mapping
void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

// Parameterized mapping
void MapFrom<TParam>(ParameterSlot<TParam> parameter,
    Expression<Func<TSource, TParam, TMember>> sourceExpression);
```

The parameterized `MapFrom` stores in `PropertyMap`:
- `HasParameterizedSource = true`
- `ParameterSlot` — reference to the slot
- `ParameterizedSourceExpression` — two-parameter lambda `(src, paramValue)`

#### PropertyMap

Stores the metadata for mapping a single property:

```csharp
public sealed class PropertyMap
{
    public PropertyInfo DestinationProperty { get; }
    public LambdaExpression? SourceExpression { get; }      // standard MapFrom
    public bool IsIgnored { get; }
    public bool HasParameterizedSource { get; }              // parameterized MapFrom
    public IParameterSlot? ParameterSlot { get; }
    public LambdaExpression? ParameterizedSourceExpression { get; }
}
```

#### MappingConfigurationBuilder

Accumulates profiles and calls `Build()`:

```csharp
var builder = new MappingConfigurationBuilder();
builder.AddProfile<MyProfile>();              // explicit profile
builder.AddProfiles(typeof(X).Assembly);      // assembly scanning
var config = builder.Build();                 // → MapperConfiguration (singleton)
```

---

### 2. Compilation Layer

**Purpose**: transform `PropertyMap` metadata into `Expression<Func<S, D>>` and `Func<S, D>`.

#### ProjectionCompiler

Central class. Algorithm:

1. Creates `ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "src")`
2. For each `PropertyMap`, builds a `MemberBinding`:
   - **Parameterized mapping**: inserts `holder.property` (closure pattern) via `ParameterReplacer`
   - **Standard MapFrom**: inserts `sourceParam` into the user lambda via `ParameterReplacer`
   - **Convention**: tries `DefaultNameConvention`, then `FlatteningConvention`
3. Automatically applies conventions to all writable destination properties not specified in `ForMember`
4. Assembles `Expression.MemberInit(Expression.New(typeof(TDest)), bindings)`
5. Wraps in `Expression.Lambda<Func<S, D>>(body, sourceParam)`

Returns an immutable `CompilationResult`:

```csharp
internal sealed record CompilationResult(
    LambdaExpression Projection,
    Type? ClosureHolderType,
    object? DefaultClosureHolder,
    IReadOnlyDictionary<string, PropertyInfo>? HolderPropertyMap);
```

#### ParameterReplacer

Simple `ExpressionVisitor` that replaces one `ParameterExpression` with another `Expression`:

```csharp
// Replaces the user lambda's parameter with the global sourceParam
body = ParameterReplacer.Replace(lambda.Body, lambda.Parameters[0], sourceParam);
```

Used in two scenarios:
- Unifying `sourceParam` from different `MapFrom` lambdas into a single expression
- Replacing the `TParam` parameter with `MemberAccess(holder, property)` for parameterization

#### InMemoryCompiler

Takes a `LambdaExpression`, wraps it in a null-check, and compiles:

```csharp
// If source != null → evaluate projection, else → default(TDest)
Expression.Condition(
    Expression.Equal(sourceParam, Expression.Constant(null)),
    Expression.Default(destType),
    projectionExpr.Body);
```

Calls `Expression.Compile()` to produce a `Func<S, D>` delegate.

#### ClosureValueInjector

`ExpressionVisitor` that replaces `ConstantExpression` nodes matching the closure holder type with a new instance containing updated parameter values:

```csharp
protected override Expression VisitConstant(ConstantExpression node)
{
    if (node.Type == _holderType)
        return Expression.Constant(_newHolderInstance, _holderType);
    return base.VisitConstant(node);
}
```

**Key property**: the expression tree shape does not change — only the value inside the `ConstantExpression` changes. EF Core sees an identical structure and reuses the cached query plan.

#### MapperConfiguration

Frozen singleton. During construction, iterates all profiles, calls `ProjectionCompiler.CompileProjection()` and `InMemoryCompiler.CompileDelegate()`, builds immutable `TypeMap` instances and stores them in `ConcurrentDictionary<TypePair, TypeMap>`:

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

Fully immutable compiled mapping container. All properties are get-only, set in the constructor:

```csharp
public sealed class TypeMap
{
    public TypePair TypePair { get; }
    public IReadOnlyList<PropertyMap> PropertyMaps { get; }
    public LambdaExpression? ProjectionExpression { get; }   // for EF Core
    public Delegate? CompiledDelegate { get; }               // for in-memory
    public Type? ClosureHolderType { get; }                  // for parameterization
    public object? DefaultClosureHolder { get; }
    internal IReadOnlyDictionary<string, PropertyInfo>? HolderPropertyMap { get; }
}
```

`HolderPropertyMap` caches the `PropertyInfo` dictionary so that `Type.GetProperty()` is never called per-request.

#### TypePair

Cache key for `ConcurrentDictionary`:

```csharp
public readonly record struct TypePair(Type SourceType, Type DestinationType);
```

---

### 3. Parameters Layer

**Purpose**: implement parameterized projections via a closure pattern that EF Core natively translates to SQL parameters.

#### ParameterSlot&lt;T&gt;

Parameter declaration in a profile:

```csharp
public sealed class ParameterSlot<T> : IParameterSlot
{
    public string Name { get; }
    public Type ValueType => typeof(T);
    public Guid Id { get; } = Guid.NewGuid();
}
```

#### ClosureHolderFactory

Dynamically generates a POCO type via `System.Reflection.Emit` (`TypeBuilder`, `FieldBuilder`, `PropertyBuilder`, `ILGenerator`):

```
ParameterSlot<string>("lang") + ParameterSlot<int>("limit")
    ↓ ClosureHolderFactory
class ClosureHolder_1 {
    public string lang { get; set; }
    public int limit { get; set; }
}
```

Algorithm:
1. `AssemblyBuilder.DefineDynamicAssembly()` — once, static
2. `ModuleBuilder.DefineType()` — per unique parameter set
3. Per slot: `DefineField` + `DefineProperty` + getter IL (`Ldarg_0`, `Ldfld`, `Ret`) + setter IL (`Ldarg_0`, `Ldarg_1`, `Stfld`, `Ret`)
4. `TypeBuilder.CreateType()` — finalization
5. Result cached in `ConcurrentDictionary` keyed by `"name:type|name:type"`

Returns `HolderTypeInfo`:
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

`IParameterBinder` implementation — a simple dictionary for passing values at query time:

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

### 4. Runtime Layer

**Purpose**: provide `IMapper` for in-memory mapping and `IProjectionProvider` for EF Core projections.

#### Mapper

Stateless. Retrieves `TypeMap` from `MapperConfiguration`, casts `CompiledDelegate` to `Func<S, D>`, and invokes:

```csharp
public TDest Map<TSource, TDest>(TSource source)
{
    var typeMap = _configuration.GetTypeMap<TSource, TDest>();
    var func = (Func<TSource, TDest>)typeMap.CompiledDelegate;
    return func(source);
}
```

Performance is in the nanosecond range (single compiled delegate call).

#### ProjectionProvider

Two modes:
- **Without parameters**: returns the cached `Expression<Func<S, D>>` from `TypeMap`
- **With parameters**:
  1. Creates a new closure holder instance via `Activator.CreateInstance()`
  2. Populates properties from `ParameterBinder.Values` using the cached `HolderPropertyMap`
  3. Calls `ClosureValueInjector.InjectParameters()` — swaps `ConstantExpression` in the tree
  4. Returns a new expression with identical structure but new values

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

### 5. Extensions

#### ServiceCollectionExtensions

```csharp
public static IServiceCollection AddMapping(
    this IServiceCollection services,
    Action<MappingConfigurationBuilder>? configure = null,
    params Assembly[] profileAssemblies)
```

What it does:
1. Creates `MappingConfigurationBuilder`
2. Calls `configure?.Invoke(builder)` for manual configuration
3. Scans provided assemblies: finds all `MappingProfile` subclasses
4. Calls `builder.Build()` — compiles all mappings
5. Runs `ConfigurationValidator.Validate()` — eager validation
6. Registers as **Singleton**: `MapperConfiguration`, `IMapper`, `IProjectionProvider`

#### QueryableExtensions

Three levels of API, from simplest to most explicit:

```csharp
// 1. Single generic parameter (recommended) — TSource inferred at runtime
source.ProjectTo<ProductDto>();
source.ProjectTo<ProductDto>(p => p.Set("lang", "ru"));

// 2. Two generic parameters — TSource explicit, no DI injection needed
source.ProjectTo<Product, ProductDto>();
source.ProjectTo<Product, ProductDto>(p => p.Set("lang", "ru"));

// 3. Explicit provider — full control, useful for testing
source.ProjectTo<Product, ProductDto>(projectionProvider);
source.ProjectTo<Product, ProductDto>(projectionProvider, p => p.Set("lang", "ru"));
```

Options 1 and 2 use a static `ProjectionProviderAccessor` initialized at `AddMapping()`. Option 3 accepts an explicit `IProjectionProvider` for backward compatibility and testability.

The single-generic overloads cache `Queryable.Select` `MethodInfo` per `(TSource, TDest)` pair via `ConcurrentDictionary` for optimal performance.

---

### 6. Validation

#### ConfigurationValidator

Checks at startup:
- Type compatibility for all explicit `ForMember` mappings
- Detects numeric conversions (`int` -> `long`, `float` -> `double`, etc.)
- Handles nullable wrapping (`int` -> `int?`)
- Does not warn about properties resolvable by conventions

#### MappingValidationException

Thrown on error with the full list:

```
Mapping configuration validation failed with 2 error(s):
  1. [Product -> ProductDto] Property 'Price': source type 'String' is not assignable to destination type 'Decimal'.
  2. [Order -> OrderDto] Property 'Status': source type 'Int32' is not assignable to destination type 'String'.
```

---

## How Mapping Works — Step by Step

### Configuration Phase (once at startup)

```
1. User: CreateMap<Product, ProductDto>()
             .ForMember(d => d.Name, o => o.MapFrom(s => s.Title))

2. TypeMapBuilder:
   PropertyMap { DestinationProperty: "Name", SourceExpression: s => s.Title }

3. MappingConfigurationBuilder.Build() → MapperConfiguration:

4. ProjectionCompiler.CompileProjection():
   4a. sourceParam = Expression.Parameter(typeof(Product), "src")
   4b. ForMember "Name": ParameterReplacer(s => s.Title, s → src) → src.Title
   4c. Convention "Id": DefaultNameConvention → src.Id
   4d. Convention "Price": DefaultNameConvention → src.Price
   4e. MemberInit: new ProductDto { Name = src.Title, Id = src.Id, Price = src.Price }
   4f. Lambda: src => new ProductDto { Name = src.Title, Id = src.Id, Price = src.Price }

5. InMemoryCompiler.CompileDelegate():
   5a. Null-check wrapper
   5b. Expression.Compile() → Func<Product, ProductDto>

6. TypeMap: stores both Expression and Delegate
```

### Execution Phase (every call)

```
// In-memory
mapper.Map<Product, ProductDto>(product)
  → TypeMap.CompiledDelegate → (Func<Product, ProductDto>)(product) → dto

// EF Core
dbContext.Products.ProjectTo<ProductDto>()
  → ProjectionProviderAccessor.Instance.GetProjection(source.ElementType, typeof(ProductDto))
  → LambdaExpression
  → Expression.Call(Queryable.Select, ...) via cached MethodInfo
  → EF Core translates to SQL:
    SELECT [p].[Title] AS [Name], [p].[Id], [p].[Price] FROM [Products] AS [p]
```

---

## How Parameterized Projections Work — Step by Step

This is the library's key innovation. Common approaches (string interpolation, constant replacement) break EF Core's query plan cache. MyAutoMapper uses the closure pattern — the same one the C# compiler generates for closures.

### Configuration Phase

```
1. User:
   var lang = DeclareParameter<string>("lang");
   CreateMap<Product, ProductViewModel>()
       .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
           (src, l) => l == "ru" ? src.NameRu : src.NameUz))

2. ClosureHolderFactory (Reflection.Emit):
   Generates a dynamic type:
   class ClosureHolder_1 { public string lang { get; set; } }

3. ProjectionCompiler:
   3a. holderInstance = new ClosureHolder_1()   // default instance
   3b. holderConstant = Expression.Constant(holderInstance, typeof(ClosureHolder_1))
   3c. holderPropertyAccess = Expression.Property(holderConstant, "lang")
       // this is MemberAccess(Constant(holder), "lang") — the closure pattern!
   3d. Substitutes into the user lambda:
       l → holderPropertyAccess
       src → sourceParam
   3e. Result:
       src => holderInstance.lang == "ru" ? src.NameRu : src.NameUz
```

### Query Phase

```
1. User:
   dbContext.Products.ProjectTo<ProductViewModel>(p => p.Set("lang", "ru"))

2. ParameterBinder: { "lang": "ru" }

3. ProjectionProvider:
   3a. newHolder = new ClosureHolder_1()
   3b. newHolder.lang = "ru"  // via cached HolderPropertyMap
   3c. ClosureValueInjector.InjectParameters():
       Walks the tree, finds ConstantExpression(oldHolder) → replaces with ConstantExpression(newHolder)
   3d. Tree shape is IDENTICAL — only the value inside Constant changed

4. EF Core receives:
   src => newHolder.lang == "ru" ? src.NameRu : src.NameUz
   ↑ MemberAccess(Constant(holder), "lang") — standard closure capture!

5. EF Core translates:
   SELECT CASE WHEN @__lang_0 = 'ru' THEN "p"."NameRu" ELSE "p"."NameUz" END
   FROM "Products" AS "p"
   -- @__lang_0 = 'ru' — SQL parameter, not an inlined constant!

6. On the next call with lang="uz":
   - A new holder is created with lang="uz"
   - Tree shape is the same → EF Core REUSES the query plan
   - Only the parameter value changes: @__lang_0 = 'uz'
```

---

## Convention-Based Auto-Mapping

When `CreateMap<S, D>()` is called without `ForMember` for every property of `D`, the `ProjectionCompiler` automatically tries to find a match.

### DefaultNameConvention

Looks for a property in `TSource` with the same name (case-insensitive):

```csharp
Source.Name → Dest.Name
Source.Id   → Dest.Id
Source.Price → Dest.Price
```

Supports nullable wrapping: `int` -> `int?`.

### FlatteningConvention

Recursively flattens nested objects by name concatenation:

```csharp
Source.Address.Street  → Dest.AddressStreet
Source.Address.City    → Dest.AddressCity
Source.Address.ZipCode → Dest.AddressZipCode
```

Algorithm:
1. Takes the destination property name (e.g., `AddressCity`)
2. Looks for a prefix property in `TSource` (`Address`)
3. Recursively searches for the remainder (`City`) in the found property's type
4. Maximum depth: 5 levels (stack overflow protection)
5. Sorts properties by name length (longest match first)

### Priority

Explicit `ForMember` always overrides conventions:

```csharp
CreateMap<Source, Dest>()
    .ForMember(d => d.Name, o => o.MapFrom(s => s.Title)); // Title, not Name
```

---

## Fluent API

### MappingProfile

```csharp
public abstract class MappingProfile
{
    // Create a mapping between types
    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>();

    // Declare a runtime parameter for parameterized projections
    protected ParameterSlot<T> DeclareParameter<T>(string name);
}
```

### ITypeMappingExpression&lt;TSource, TDest&gt;

```csharp
// Configure mapping for a specific property
.ForMember(d => d.Prop, options => ...)

// Ignore a property (do not map)
.Ignore(d => d.Prop)

// Custom constructor
.ConstructUsing(src => new Dest { ... })

// Create a reverse mapping (Dest → Source)
.ReverseMap()
```

### IMemberOptions&lt;TSource, TDest, TMember&gt;

```csharp
// Map from an expression
options.MapFrom(src => src.SomeProp)

// Map with a runtime parameter
options.MapFrom(langSlot, (src, lang) => lang == "ru" ? src.NameRu : src.NameUz)

// Ignore
options.Ignore()
```

---

## DI Integration

```csharp
// Auto-scan assembly — finds all MappingProfile subclasses
builder.Services.AddMapping(typeof(Program).Assembly);
```

With manual configuration:

```csharp
builder.Services.AddMapping(
    cfg => cfg.AddProfile<MyCustomProfile>(),
    typeof(SomeProfile).Assembly);
```

Registers as **Singleton**:
- `MapperConfiguration` — frozen configuration
- `IMapper` — stateless mapper
- `IProjectionProvider` — projection provider + static `ProjectionProviderAccessor`

The static accessor is initialized eagerly during `AddMapping()`, enabling `ProjectTo<TDest>()` calls without explicit DI injection.

All mappings are validated at registration. On errors: `MappingValidationException` with the full list of issues.

---

## Example: Web API with Localization

The project `samples/MyAutoMapper.WebApiSample` is a working ASP.NET Core Web API example with EF Core SQLite and parameterized localization.

### Profile with Parameterization

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

No need to inject `IProjectionProvider` — the static accessor resolves it automatically.

### Program.cs

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=sample.db"));

builder.Services.AddMapping(typeof(Program).Assembly);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
```

### Output

`GET /api/products?lang=ru`:
```json
[
  {"id": 1, "localizedName": "iPhone 16 Pro", "localizedDescription": "Newest Apple smartphone", "price": 12990000.0},
  {"id": 2, "localizedName": "Samsung Galaxy S25", "localizedDescription": "Flagship Samsung smartphone", "price": 10490000.0}
]
```

`GET /api/products?lang=uz`:
```json
[
  {"id": 1, "localizedName": "iPhone 16 Pro", "localizedDescription": "Eng yangi Apple smartfoni", "price": 12990000.0}
]
```

SQL generated by EF Core:
```sql
SELECT "p"."NameRu" AS "LocalizedName", "p"."DescriptionRu" AS "LocalizedDescription",
       "p"."Id", "p"."Price"
FROM "Products" AS "p"
```

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

### Unit Tests

```bash
dotnet test tests/MyAutoMapper.UnitTests
```

| Test File | What It Checks |
|---|---|
| `MappingProfileTests` | Profile creation, TypeMaps accumulation |
| `MapperConfigurationTests` | Compilation, caching, error handling |
| `MapperTests` | In-memory mapping, null source |
| `ProjectionProviderTests` | Projections without and with parameters |
| `ConventionMappingTests` | Conventions, flattening, ReverseMap |
| `ConfigurationValidatorTests` | Type validation, error detection |

### Integration Tests

```bash
dotnet test tests/MyAutoMapper.IntegrationTests
```

| Test File | What It Checks |
|---|---|
| `ProjectToTests` | ProjectTo generates correct SQL |
| `ParameterizedProjectionTests` | Parameterization: lang=en, lang=fr, unknown |
| `ServiceCollectionTests` | DI registration, singleton lifetimes |

---

## Benchmarks

Compares **MyAutoMapper** vs **AutoMapper** (16.1.1) vs **Mapster** (10.0.3) vs manual mapping.

### Available Benchmarks

| Benchmark | What It Measures |
|---|---|
| `SimpleMappingBenchmark` | Flat mapping, 3 properties |
| `ComplexMappingBenchmark` | Flat mapping, 10 properties |
| `FlatteningBenchmark` | Nested object flattening |
| `ConfigurationBenchmark` | Configuration cost (Build/Compile) |

### Running Benchmarks

```bash
cd tests/MyAutoMapper.Benchmarks

# All benchmarks (5-15 min)
dotnet run -c Release -- --filter *

# Specific benchmark
dotnet run -c Release -- --filter *SimpleMappingBenchmark*
dotnet run -c Release -- --filter *FlatteningBenchmark*

# Quick run (less accurate, 1-3 min)
dotnet run -c Release -- --filter * --job short

# List all benchmarks
dotnet run -c Release -- --list flat

# Export results
dotnet run -c Release -- --filter * --exporters markdown
dotnet run -c Release -- --filter * --exporters json
dotnet run -c Release -- --filter * --exporters html
```

Results are saved to `BenchmarkDotNet.Artifacts/results/`.

> **Important**: always run with `-c Release`. Debug builds produce inaccurate results.

---

## Building and Running

```bash
# Build the entire solution
dotnet build

# Run all tests
dotnet test

# Run the Web API example
cd samples/MyAutoMapper.WebApiSample
dotnet run

# Run benchmarks
cd tests/MyAutoMapper.Benchmarks
dotnet run -c Release -- --filter *

dotnet pack -c Release
dotnet nuget push bin/Release/MyAutoMapper.1.0.0.nupkg -k <ваш_api_key> -s https://api.nuget.org/v3/index.json
```

---

## Technologies Used

| Technology | Where It's Used |
|---|---|
| **.NET 10 / C# 14** | Entire codebase (`LangVersion preview`, `.slnx` format) |
| **Expression Trees** (`System.Linq.Expressions`) | Generating `Expression<Func<S,D>>` for EF Core and `Func<S,D>` delegates |
| **ExpressionVisitor** | `ParameterReplacer`, `ClosureValueInjector` — tree traversal and transformation |
| **System.Reflection.Emit** | `ClosureHolderFactory` — dynamic POCO type generation |
| **ConcurrentDictionary** | Caching `TypeMap`, `HolderTypeInfo` |
| **Central Package Management** | `Directory.Packages.props` — unified NuGet version management |
| **Microsoft.Extensions.DI** | `AddMapping()`, `IServiceCollection`, Singleton |
| **EF Core + SQLite** | Integration tests, Web API example |
| **BenchmarkDotNet** | Benchmarks vs AutoMapper, Mapster |
| **xUnit + FluentAssertions** | Unit and integration tests |
| **Swashbuckle** | Swagger UI in the Web API example |

## License

MIT
