# Architectural plan for a custom AutoMapper alternative in .NET 10

**A runtime expression-tree-based mapping library can deliver AutoMapper's flexibility with cleaner parameterized projections that EF Core translates directly to SQL.** The architecture described below combines AutoMapper's proven Profile/fluent configuration model with a first-class parameter system that solves the hardest problem in object mapping: passing runtime context into `IQueryable.Select()` projections without breaking SQL translation. The design leverages .NET 10's JIT improvements, C# 14 extension members, and a closure-based parameter injection pattern that EF Core natively parameterizes.

This plan covers the complete library architecture across five phases: configuration API, expression tree compilation engine, parameterized projection system, DI integration, and EF Core Projectables interop.

---

## Core architecture and class hierarchy

The library follows AutoMapper's proven layered architecture but simplifies the compilation pipeline and makes runtime parameters a first-class concept rather than an afterthought bolted onto `ResolutionContext`.

**Three architectural layers** compose the system. The **Configuration Layer** (user-facing) includes `MappingProfile`, `TypeMapBuilder<TSource, TDest>`, and `MemberMapBuilder<TSource, TDest, TMember>` — these implement the fluent API. The **Compilation Layer** (internal) holds `MapperConfiguration`, `TypeMap`, `ProjectionCompiler`, and `ParameterInjectionVisitor` — these transform configuration into expression trees. The **Runtime Layer** (execution) contains `Mapper`, `ProjectionProvider`, and `MappingContext` — these execute compiled mappings and serve projections to EF Core.

```
MappingProfile (user-defined, inherits ProfileBase)
  └─ CreateMap<S,D>() → TypeMapBuilder<S,D> (fluent config)
       └─ .ForMember(dest => dest.X, opt => opt.MapFrom(src => src.Y))
            └─ MemberMapBuilder<S,D,TMember> (nested fluent config)

MapperConfiguration (frozen, immutable after Build())
  └─ TypeMap[] (compiled from all profiles)
       ├─ PropertyMap[] (per-member mapping rules)
       ├─ Expression<Func<TSource, TDest>> (projection expression)
       ├─ Func<TSource, TDest> (compiled in-memory delegate)
       └─ ParameterSlot[] (declared runtime parameter slots)

Mapper : IMapper (lightweight, stateless, singleton-safe)
  └─ Map<S,D>(source) → executes compiled delegate
  └─ Project<S,D>(params?) → returns Expression<Func<S,D>> for IQueryable
```

The critical design decision is **separating the projection expression tree from the in-memory mapping delegate**. AutoMapper's `CreateProjection` vs. `CreateMap` split (introduced in v11) proved this separation essential. The in-memory delegate can use arbitrary C# (method calls, services, complex logic), while the projection expression must stay within EF Core's translatable subset: property access, arithmetic, comparisons, ternary conditionals, `MemberInitExpression`, and navigation properties.

---

## The fluent configuration API in detail

The fluent API mirrors AutoMapper's ergonomics but uses interface segregation to enforce valid configuration sequences and make runtime parameters explicit.

```csharp
public abstract class MappingProfile
{
    private readonly List<ITypeMapConfiguration> _typeMaps = [];

    protected ITypeMappingExpression<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var config = new TypeMapBuilder<TSource, TDest>();
        _typeMaps.Add(config);
        return config;
    }

    // Declare a parameter slot available to all maps in this profile
    protected ParameterSlot<T> DeclareParameter<T>(string name)
        => new(name);
}

public interface ITypeMappingExpression<TSource, TDest>
{
    ITypeMappingExpression<TSource, TDest> ForMember<TMember>(
        Expression<Func<TDest, TMember>> destinationMember,
        Action<IMemberOptions<TSource, TDest, TMember>> options);

    ITypeMappingExpression<TSource, TDest> Ignore(
        Expression<Func<TDest, object>> destinationMember);

    ITypeMappingExpression<TSource, TDest> ConstructUsing(
        Expression<Func<TSource, TDest>> constructor);
}

public interface IMemberOptions<TSource, TDest, TMember>
{
    void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);

    // Parameterized mapping — the key differentiator
    void MapFrom<TParam>(
        ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression);
}
```

The `ParameterSlot<T>` type is the first-class parameter mechanism. Unlike AutoMapper's `Items` dictionary (untyped, stringly-keyed, invisible to the expression tree compiler), `ParameterSlot<T>` carries type information and a name that the expression compiler uses to wire up closure-based parameter injection. A concrete profile looks like this:

```csharp
public class ProductProfile : MappingProfile
{
    private readonly ParameterSlot<string> _lang = DeclareParameter<string>("lang");

    public ProductProfile()
    {
        CreateMap<Product, ProductDto>()
            .ForMember(d => d.Id, opt => opt.MapFrom(s => s.Id))
            .ForMember(d => d.Name, opt => opt.MapFrom(_lang,
                (src, lang) => lang == "en" ? src.NameEn :
                               lang == "fr" ? src.NameFr :
                               src.NameDefault))
            .ForMember(d => d.Price, opt => opt.MapFrom(s => s.Price));
    }
}
```

**Extracting `PropertyInfo` from member-selector lambdas** requires handling the `UnaryExpression` (Convert) node that C# inserts for value-type properties boxed to `object`. The implementation unwraps `UnaryExpression.Operand` to reach the `MemberExpression`, then casts `MemberExpression.Member` to `PropertyInfo`. For nested paths like `d => d.Address.City`, the expression is traversed recursively through `MemberExpression.Expression`.

---

## Expression tree compilation engine

The compilation engine is the library's core. It transforms fluent configuration into two artifacts per type pair: an `Expression<Func<TSource, TDest>>` for IQueryable projection, and a compiled `Func<TSource, TDest>` delegate for in-memory mapping.

**Building `MemberInitExpression` trees** follows the standard pattern. For each `PropertyMap` in a `TypeMap`, the compiler creates an `Expression.Bind(destProperty, sourceExpression)` node. All bindings combine into `Expression.MemberInit(Expression.New(typeof(TDest)), bindings)`, wrapped in `Expression.Lambda<Func<TSource, TDest>>(body, sourceParam)`.

```csharp
internal class ProjectionCompiler
{
    public Expression<Func<TSource, TDest>> CompileProjection<TSource, TDest>(
        TypeMap typeMap, ParameterContext? parameters = null)
    {
        var sourceParam = Expression.Parameter(typeof(TSource), "src");
        var bindings = new List<MemberBinding>();

        foreach (var propertyMap in typeMap.PropertyMaps)
        {
            Expression valueExpr = propertyMap.HasParameterizedSource
                ? BuildParameterizedBinding(propertyMap, sourceParam, parameters)
                : RebindExpression(propertyMap.SourceExpression, sourceParam);

            bindings.Add(Expression.Bind(propertyMap.DestinationProperty, valueExpr));
        }

        var body = Expression.MemberInit(
            Expression.New(typeof(TDest)), bindings);
        return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam);
    }
}
```

**The parameter rebinding step** is critical. Each `ForMember` configuration stores a `LambdaExpression` whose parameters reference the profile's source parameter. During compilation, a `ParameterReplacer` (an `ExpressionVisitor`) substitutes the lambda's parameter with the actual `sourceParam` used in the projection. This is the same technique AutoMapper's `TypeMapPlanBuilder` uses internally.

```csharp
internal class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _old;
    private readonly Expression _replacement;

    protected override Expression VisitParameter(ParameterExpression node)
        => node == _old ? _replacement : base.VisitParameter(node);
}
```

**Caching strategy**: The compiled projection expressions and delegates are cached in a `ConcurrentDictionary<TypePair, TypeMap>` within `MapperConfiguration`. For non-parameterized mappings, both the expression and delegate are built once and reused. For parameterized mappings, the **expression tree structure** is cached, but the closure object holding parameter values is swapped at runtime (see next section). This mirrors EF Core's own query caching — same tree shape, different parameter values.

---

## How parameterized projections work with EF Core

This is the hardest architectural problem. **EF Core translates closure-captured variables into SQL parameters**, but `Expression.Constant(value)` with varying values causes query plan cache pollution. The solution: build expressions that reference properties on a closure object, then swap the closure object at runtime.

The `ParameterSlot<T>` declared in profiles compiles into a **parameter holder class** — a simple POCO whose properties correspond to each declared parameter:

```csharp
// Generated at configuration time for ProductProfile's parameters:
internal class ProductProfile_Parameters
{
    public string lang { get; set; }
}
```

During projection compilation, the `MapFrom(_lang, (src, lang) => ...)` expression gets rewritten. The `lang` parameter in the user's lambda is replaced with a `MemberExpression` that accesses `holder.lang` on a `ConstantExpression` referencing an instance of `ProductProfile_Parameters`:

```csharp
// What the user writes:
(src, lang) => lang == "en" ? src.NameEn : src.NameFr

// What the compiler produces (conceptual):
// holder is a ProductProfile_Parameters instance captured as a closure
src => holder.lang == "en" ? src.NameEn : src.NameFr

// Expression tree structure:
// ConditionalExpression(
//   test: Equal(
//     MemberAccess(Constant(holder), "lang"),   // ← closure pattern
//     Constant("en")),                          // ← string literal, OK
//   ifTrue: MemberAccess(Parameter("src"), "NameEn"),
//   ifFalse: MemberAccess(Parameter("src"), "NameFr"))
```

**EF Core recognizes this closure pattern** — a `MemberExpression` accessing a property on a `ConstantExpression` whose value is a reference type — and extracts the value as a SQL parameter (`@__lang_0`). The generated SQL becomes `CASE WHEN @__lang_0 = N'en' THEN [p].[NameEn] ELSE [p].[NameFr] END`. Because the expression tree shape remains identical regardless of the `lang` value, EF Core caches and reuses the compiled query plan.

**The runtime API** for providing parameter values creates a fresh holder instance:

```csharp
// Extension method on IQueryable
public static IQueryable<TDest> ProjectTo<TSource, TDest>(
    this IQueryable<TSource> source,
    IProjectionProvider projections,
    Action<IParameterBinder>? parameters = null)
{
    var binder = new ParameterBinder();
    parameters?.Invoke(binder);

    // Get cached expression template and inject parameter values
    var expression = projections.GetProjection<TSource, TDest>(binder);
    return source.Select(expression);
}

// Usage:
var dtos = context.Products
    .ProjectTo<Product, ProductDto>(projections, p => p
        .Set("lang", "en"))
    .ToListAsync();
```

Internally, `GetProjection` uses the `ClosureValueInjector` — an `ExpressionVisitor` that walks the cached expression template and replaces the closure holder's `ConstantExpression` node with a new `ConstantExpression` pointing to a freshly constructed holder containing the caller's parameter values. This is exactly how AutoMapper's `ProjectTo` handles parameters internally, but the custom library makes it type-safe and explicit.

---

## In-memory mapping vs. projection: two compilation paths

The architecture maintains **strict separation** between in-memory mapping and IQueryable projection, following AutoMapper v11's lesson.

**In-memory mapping** (`Mapper.Map<S,D>(source)`) compiles expression trees into `Func<TSource, TDest>` delegates via `Expression.Compile()`. These delegates can use the full C# feature set — method calls, service injection, complex branching, exception handling. The compiled delegate is cached as a static field per type pair. Runtime parameters are passed through a `MappingContext` object (similar to AutoMapper's `ResolutionContext`) that the delegate receives as a captured closure.

**IQueryable projection** (`source.ProjectTo<S,D>()`) returns the raw `Expression<Func<TSource, TDest>>` — never compiled, always passed as an expression tree to EF Core's query provider. This expression must contain only EF Core-translatable nodes. The `ProjectionCompiler` enforces this by rejecting configurations that use non-translatable constructs (method calls on custom types, service references, etc.) and emitting clear error messages at configuration validation time.

Key translatable patterns the compiler produces:

- `MemberInitExpression` with `MemberAssignment` bindings → SQL `SELECT` columns
- `ConditionalExpression` (ternary `?:`) → SQL `CASE WHEN`
- `BinaryExpression` (arithmetic, comparison) → SQL operators
- `MemberExpression` on navigation properties → SQL `JOIN`
- Closure-captured values → SQL `@parameters`
- `string` methods (`.Length`, `.Contains()`, `.ToUpper()`) → SQL functions

Patterns the compiler rejects with validation errors:

- Custom method calls (`Expression.Call` to user methods)
- `Expression.Invoke` (sub-expression invocation)
- `Expression.Block` (statement blocks)
- Service/DI resolution during projection

---

## DI integration with Microsoft.Extensions.DependencyInjection

The registration model follows Mapster's proven pattern but improves on it by making `MapperConfiguration` truly immutable after construction.

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMapping(
        this IServiceCollection services,
        Action<MappingConfigurationBuilder>? configure = null,
        params Assembly[] profileAssemblies)
    {
        var builder = new MappingConfigurationBuilder();
        configure?.Invoke(builder);

        // Auto-discover profiles via assembly scanning
        foreach (var assembly in profileAssemblies)
        {
            var profileTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(MappingProfile)) && !t.IsAbstract);
            foreach (var type in profileTypes)
                builder.AddProfile(type);
        }

        // Build frozen configuration — singleton, thread-safe
        var configuration = builder.Build(); // validates all mappings
        services.AddSingleton(configuration);
        services.AddSingleton<IMapper>(sp => configuration.CreateMapper());
        services.AddSingleton<IProjectionProvider>(sp => configuration.CreateProjectionProvider());

        return services;
    }
}
```

**Profiles are registered as Singletons** indirectly — their configuration is consumed during `Build()` and baked into the immutable `MapperConfiguration`. The `MapperConfiguration`, `IMapper`, and `IProjectionProvider` instances are all singletons. This eliminates the lifetime management issues that plague Mapster's `ServiceMapper` (which requires scoped registration to resolve scoped services during mapping).

The `Build()` call performs **eager validation**: iterating all type maps, verifying every destination member is mapped, checking for type mismatches, and pre-compiling both projection expressions and in-memory delegates. Any misconfiguration throws at startup, not at first use — following the "fail fast" principle that AutoMapper's `AssertConfigurationIsValid()` requires users to call manually.

---

## Integrating with EntityFrameworkCore.Projectables

EF Core Projectables replaces `[Projectable]`-annotated property/method calls with their source-generated expression tree equivalents inside EF Core's query pipeline. A custom mapper can integrate through two approaches.

**Approach 1: Direct projection via `IQueryable.Select()`** (recommended). The mapper's `ProjectTo<S,D>()` method produces a standard `Expression<Func<S,D>>` containing `MemberInitExpression` nodes. When this expression is passed to `source.Select(expression)`, EF Core translates it directly — no Projectables involvement needed. This is the simplest path and works without any Projectables dependency.

**Approach 2: `UseMemberBody` bridge for entity-level projectable properties**. When entities have `[Projectable]` computed properties that the mapper references, Projectables expands those property accesses before EF Core translation. This works automatically — if a mapping expression references `src.FullName` and `FullName` is `[Projectable]`, the Projectables interceptor (whether `IQueryTranslationPreprocessor` in Limited mode or `CustomQueryCompiler` in Full mode) rewrites that access into the generated expression before SQL translation.

```csharp
// Entity with Projectable property
public class User
{
    public string FirstName { get; set; }
    public string LastName { get; set; }

    [Projectable]
    public string FullName => FirstName + " " + LastName;
}

// Mapper configuration references FullName naturally
CreateMap<User, UserDto>()
    .ForMember(d => d.DisplayName, opt => opt.MapFrom(s => s.FullName));

// The compiled projection expression contains:
// src => new UserDto { DisplayName = src.FullName }
//
// Projectables rewrites this to:
// src => new UserDto { DisplayName = src.FirstName + " " + src.LastName }
//
// EF Core translates to:
// SELECT [u].[FirstName] + N' ' + [u].[LastName] AS [DisplayName] FROM [Users] AS [u]
```

**Approach 3: Advanced — generating expressions that Projectables can consume via `UseMemberBody`**. The mapper could generate `Expression<Func<TSource, TDest>>` static properties on partial entity classes, then entities reference them with `[Projectable(UseMemberBody = nameof(MapExpr))]`. This requires source generation on the mapper's side and is only worthwhile for scenarios where entity-level projectable properties must delegate to mapper-generated expressions. Current Projectables limitations make this fragile — **`UseMemberBody` with `Expression<Func<>>` targets can be in separate files via partial classes**, but there's no formal plugin API for registering external expression providers at runtime.

The practical recommendation: **use Approach 1 for mapper-driven projections** (direct `Select()`) and **let Approach 2 handle Projectables-decorated entity properties naturally**. No special integration code is needed — the two systems compose cleanly because they both operate on standard `System.Linq.Expressions` trees.

---

## Leveraging .NET 10 and C# 14 features

Several .NET 10 capabilities directly benefit the library's architecture.

**C# 14 extension members** enable an ergonomic API surface without polluting source types. Instead of extension methods, extension properties allow `source.MapTo<Dto>()` syntax. The `field` keyword simplifies configuration builder property implementations with inline validation.

**Static abstract interface members** (stable since .NET 7, mature in .NET 10) enable compile-time mapping contracts. A `IMappable<TSelf>` interface with `static abstract Expression<Func<TSelf, TDest>> GetProjection()` allows types to self-describe their mapping rules, discoverable via constrained generics without reflection.

**.NET 10 JIT improvements** significantly benefit compiled expression delegates. **Delegate escape analysis** means mapping delegates invoked within a method can be stack-allocated (benchmarked at **3× speedup, 3× less memory**). **Array interface devirtualization** inlines collection iteration through `IEnumerable<T>`. **Improved struct register passing** eliminates memory round-trips for value-type DTO mapping. These improvements apply automatically to `Expression.Compile()` output.

**Interceptors** (stable in .NET 9.0.2xx+, used by ASP.NET Core in .NET 10) could provide an optional AOT optimization path. A source generator could detect `mapper.Map<S,D>(source)` calls at compile time and intercept them with generated, reflection-free mapping methods. This is an optional enhancement, not a core requirement, since the runtime expression tree approach is the primary design.

---

## Multi-phase implementation plan

**Phase 1 — Configuration foundation (weeks 1–3).** Build `MappingProfile`, `TypeMapBuilder<S,D>`, and `MemberMapBuilder<S,D,TMember>`. Implement `PropertyInfo` extraction from lambda expressions, handling `UnaryExpression` unwrapping for value types. Create the `ITypeMappingExpression` fluent interface with `ForMember`, `Ignore`, and `ConstructUsing`. Implement `ParameterSlot<T>` declaration. Build `MapperConfiguration` with assembly scanning, profile discovery, and eager validation. Target: profiles can be defined with the fluent API and validated.

**Phase 2 — Expression tree compilation (weeks 4–6).** Build `ProjectionCompiler` that transforms `TypeMap` configurations into `Expression<Func<S,D>>` using `MemberInitExpression` + `MemberBinding`. Implement `ParameterReplacer` visitor for rebinding lambda parameters. Build the in-memory mapping compiler that adds null checks, type conversions, and reference handling around the core property assignments. Implement `ConcurrentDictionary`-based caching for compiled expressions and delegates. Target: simple property-to-property and computed mappings work for both in-memory and projection scenarios.

**Phase 3 — Parameterized projections (weeks 7–9).** Implement the parameter holder class generation (dynamic type creation or generic parameter containers). Build `ClosureValueInjector` visitor that swaps holder instances in cached expression templates. Implement the `IProjectionProvider` API with `GetProjection<S,D>(parameterBinder)`. Build the `ProjectTo<S,D>()` IQueryable extension method. Validate that EF Core parameterizes closure-captured values correctly (generating `@param` in SQL, not inlined constants). Write integration tests against actual EF Core with SQL Server/PostgreSQL. Target: parameterized projections generate correct, cached SQL.

**Phase 4 — DI and validation (weeks 10–11).** Build `ServiceCollectionExtensions.AddMapping()` with assembly scanning. Implement eager configuration validation (unmapped members, type mismatches, circular references). Add descriptive error messages. Implement `IMapper` as a lightweight singleton wrapper. Target: full DI-integrated startup with fail-fast validation.

**Phase 5 — Projectables interop and polish (weeks 12–13).** Test Approach 2 (natural Projectables expansion of `[Projectable]` properties referenced in mapping expressions). Document integration patterns. Add configuration options for strictness levels, naming conventions, and flattening rules. Implement `ReverseMap()`. Add benchmark suite comparing against AutoMapper and Mapster for both in-memory mapping and IQueryable projection. Target: production-ready library with documented Projectables interop.

---

## Conclusion

The architecture's key innovation is treating **runtime parameters as first-class expression tree citizens** rather than ambient context. By using typed `ParameterSlot<T>` declarations that compile into closure-holder objects, the library produces expression trees that EF Core naturally parameterizes — no visitor-based constant replacement at query time, no query plan cache pollution, and no runtime type dictionaries. This solves the fundamental limitation shared by AutoMapper's `Items` dictionary (invisible to projection compiler) and Mapster's `MapContext.Current.Parameters` (untranslatable by EF Core's query provider). The closure-based approach means a single cached expression tree shape serves all parameter value combinations, with EF Core generating `CASE WHEN @__lang_0 = N'en' THEN ...` SQL that the database engine caches and reuses efficiently.