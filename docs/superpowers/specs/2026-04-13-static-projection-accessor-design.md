# Static ProjectionProvider Accessor

**Date:** 2026-04-13
**Status:** Approved

## Problem

Currently, every consumer of `ProjectTo<TSource, TDest>()` must inject `IProjectionProvider` via constructor DI and pass it explicitly to every call:

```csharp
public ProductsController(AppDbContext db, IProjectionProvider projections)
{
    _db = db;
    _projections = projections;
}

var products = await _db.Products
    .ProjectTo<Product, ProductViewModel>(_projections, p => p.Set("lang", lang))
    .ToListAsync();
```

This adds boilerplate to every controller/service that uses projections.

## Solution

Introduce a static `ProjectionProviderAccessor` singleton, initialized once during `AddMapping()` DI registration. Add new `ProjectTo` overloads that resolve the provider automatically.

### Target API

```csharp
// Without parameters
_db.Products.ProjectTo<Product, ProductViewModel>().ToListAsync();

// With parameters
_db.Products.ProjectTo<Product, ProductViewModel>(p => p.Set("lang", lang)).ToListAsync();
```

No `IProjectionProvider` injection required.

## Design

### 1. New file: `src/MyAutoMapper/Runtime/ProjectionProviderAccessor.cs`

Static class in `SmAutoMapper.Runtime` namespace:

- `internal static void SetInstance(IProjectionProvider provider)` — called once from DI registration; throws `ArgumentNullException` if null.
- `public static IProjectionProvider Instance` — returns the singleton; throws `InvalidOperationException` if not yet configured.
- Thread-safe: reference assignment is atomic, singleton is set once at startup.

### 2. Modified: `src/MyAutoMapper/Extensions/ServiceCollectionExtensions.cs`

Change the `IProjectionProvider` singleton registration to also initialize the static accessor:

```csharp
services.AddSingleton<IProjectionProvider>(sp =>
{
    var provider = sp.GetRequiredService<MapperConfiguration>().CreateProjectionProvider();
    ProjectionProviderAccessor.SetInstance(provider);
    return provider;
});
```

The DI container remains the single source of truth. The static accessor is a convenience layer.

### 3. Modified: `src/MyAutoMapper/Extensions/QueryableExtensions.cs`

Add 2 new overloads (without `IProjectionProvider` parameter):

- `ProjectTo<TSource, TDest>(this IQueryable<TSource> source)` — uses `ProjectionProviderAccessor.Instance`
- `ProjectTo<TSource, TDest>(this IQueryable<TSource> source, Action<IParameterBinder> parameters)` — same, with parameter binding

Existing overloads with explicit `IProjectionProvider` remain for backward compatibility.

### 4. Modified (sample): `samples/.../Controllers/ProductsController.cs`

- Remove `IProjectionProvider` constructor injection
- Remove `_projections` field
- Simplify all `ProjectTo` calls to omit the provider argument

## Files Changed

| File | Action |
|---|---|
| `src/MyAutoMapper/Runtime/ProjectionProviderAccessor.cs` | New |
| `src/MyAutoMapper/Extensions/QueryableExtensions.cs` | Add 2 overloads |
| `src/MyAutoMapper/Extensions/ServiceCollectionExtensions.cs` | Init accessor in DI |
| `samples/.../Controllers/ProductsController.cs` | Simplify (remove DI of IProjectionProvider) |

## Backward Compatibility

Full. Old overloads with explicit `IProjectionProvider` remain unchanged. Existing code continues to compile and work without modifications.

## Constraints

- Only one `MapperConfiguration` per application (standard for this type of library).
- `ProjectTo` without explicit provider will throw if called before `AddMapping()` / DI initialization.
