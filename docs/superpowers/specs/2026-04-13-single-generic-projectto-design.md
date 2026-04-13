# Single Generic Parameter ProjectTo

**Date:** 2026-04-13
**Status:** Approved

## Problem

Currently, `ProjectTo` requires both `TSource` and `TDest` as generic parameters:

```csharp
_db.Products.ProjectTo<Product, ProductViewModel>(p => p.Set("lang", lang))
```

`TSource` is redundant — the compiler already knows it from `IQueryable<Product>`. The goal is to simplify to:

```csharp
_db.Products.ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
```

C# does not support partial generic inference, so `TSource` must be resolved at runtime.

## Solution

Add non-generic methods to `IProjectionProvider`, cache the `Queryable.Select` `MethodInfo` per type pair, and add new extension method overloads that accept `IQueryable` (non-generic) with a single `TDest` type parameter.

### Target API

```csharp
// Without parameters
_db.Products.ProjectTo<ProductViewModel>().ToListAsync();

// With parameters
_db.Products.ProjectTo<ProductViewModel>(p => p.Set("lang", lang)).ToListAsync();
```

## Design

### 1. Modified: `src/MyAutoMapper/Runtime/IProjectionProvider.cs`

Add 2 non-generic methods:

```csharp
LambdaExpression GetProjection(Type sourceType, Type destType);
LambdaExpression GetProjection(Type sourceType, Type destType, IParameterBinder parameters);
```

These return `LambdaExpression` instead of `Expression<Func<TSource, TDest>>`. Existing generic methods remain unchanged.

### 2. Modified: `src/MyAutoMapper/Runtime/ProjectionProvider.cs`

Implement the 2 non-generic methods. Logic mirrors the generic versions:

- `GetProjection(Type, Type)` — calls `_configuration.GetTypeMap(sourceType, destType)`, returns `typeMap.ProjectionExpression` directly as `LambdaExpression`.
- `GetProjection(Type, Type, IParameterBinder)` — same but with parameter injection via `ClosureValueInjector.InjectParameters(LambdaExpression, ...)` (non-generic overload already exists).

### 3. Modified: `src/MyAutoMapper/Extensions/QueryableExtensions.cs`

**Cached MethodInfo for `Queryable.Select`:**

```csharp
private static readonly MethodInfo SelectDefinition =
    typeof(Queryable).GetMethods()
        .First(m => m.Name == "Select"
            && m.GetParameters().Length == 2
            && m.GetParameters()[1].ParameterType.GetGenericArguments()[0]
                .GetGenericArguments().Length == 2);

private static readonly ConcurrentDictionary<(Type Source, Type Dest), MethodInfo> SelectCache = new();

private static MethodInfo GetSelectMethod(Type sourceType, Type destType)
    => SelectCache.GetOrAdd((sourceType, destType),
        key => SelectDefinition.MakeGenericMethod(key.Source, key.Dest));
```

- `SelectDefinition` resolves once at class load time.
- `SelectCache` is a `ConcurrentDictionary` keyed by `(Type, Type)` tuple. `MakeGenericMethod` is called only on first access per type pair.
- Thread-safe by design.

**2 new extension methods (single generic parameter, `IQueryable` receiver):**

```csharp
public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source)
{
    var projection = ProjectionProviderAccessor.Instance
        .GetProjection(source.ElementType, typeof(TDest));
    var select = GetSelectMethod(source.ElementType, typeof(TDest));
    var call = Expression.Call(select, source.Expression, Expression.Quote(projection));
    return source.Provider.CreateQuery<TDest>(call);
}

public static IQueryable<TDest> ProjectTo<TDest>(
    this IQueryable source,
    Action<IParameterBinder> parameters)
{
    var binder = new ParameterBinder();
    parameters(binder);
    var projection = ProjectionProviderAccessor.Instance
        .GetProjection(source.ElementType, typeof(TDest), binder);
    var select = GetSelectMethod(source.ElementType, typeof(TDest));
    var call = Expression.Call(select, source.Expression, Expression.Quote(projection));
    return source.Provider.CreateQuery<TDest>(call);
}
```

All existing overloads (`ProjectTo<TSource, TDest>` with and without `IProjectionProvider`) remain unchanged for backward compatibility.

### 4. Modified (sample): `samples/.../Controllers/ProductsController.cs`

Update all `ProjectTo` calls to use single generic parameter:

```csharp
// Before:
.ProjectTo<Product, ProductViewModel>(p => p.Set("lang", lang))

// After:
.ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
```

## Files Changed

| File | Action |
|---|---|
| `src/MyAutoMapper/Runtime/IProjectionProvider.cs` | Add 2 non-generic methods |
| `src/MyAutoMapper/Runtime/ProjectionProvider.cs` | Implement non-generic methods |
| `src/MyAutoMapper/Extensions/QueryableExtensions.cs` | Add MethodInfo cache + 2 new overloads |
| `samples/.../Controllers/ProductsController.cs` | Simplify calls |

## Backward Compatibility

Full. All existing overloads remain. `IProjectionProvider` gains new methods, but this interface is only implemented inside the library (`ProjectionProvider` class), so no external consumers break.

## Performance

- `SelectDefinition` resolved once via static initializer.
- `MakeGenericMethod` result cached per `(TSource, TDest)` pair in `ConcurrentDictionary`.
- Non-generic `GetProjection` avoids generic method dispatch — direct `TypeMap` lookup by `Type` pair.
- Expression tree construction (`Expression.Call`, `Expression.Quote`) is lightweight.
