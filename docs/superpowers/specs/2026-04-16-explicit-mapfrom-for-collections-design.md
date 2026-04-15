# Explicit MapFrom for Heterogeneous Collections — Design

**Date:** 2026-04-16
**Status:** Approved (awaiting implementation plan)

## Problem

`IMemberOptions<TSource, TDest, TMember>.MapFrom` requires the lambda's
return type to match `TMember` exactly. For a member like
`CategoryViewModel.Children : List<CategoryViewModel>` the user cannot write:

```csharp
.ForMember(d => d.Children, o => o.MapFrom(src => src.Children))
```

because `src.Children` is `List<Category>`, not `List<CategoryViewModel>`.
The C# compiler reports `CS0029` / `CS1662` before the library ever sees the
expression. Today the only way to project `Children` is to rely on the
name-based convention. There is no opt-out via explicit configuration.

## Goals

1. Allow explicit `MapFrom` for collections whose element types differ when
   an element-level `TypeMap` is registered.
2. Work for both `IQueryable.ProjectTo` (EF expression trees) and
   `IMapper.Map` (runtime compiled delegates).
3. Keep the existing overload fully backward-compatible — no breaking change
   for users who already wrote `MapFrom(src => src.Scalar)`.
4. Fail fast with a clear `InvalidOperationException` when the user asks for
   an element-level mapping that cannot be resolved. Convention-based
   auto-mapping keeps its current silent-skip behaviour.

## Non-Goals

- Implicit runtime casting between element types without a registered
  `TypeMap` (covered by existing `Expression.Convert` fall-through for
  assignable types — no change).
- Re-writing the convention pipeline.
- New `MapFromCollection` helper method (rejected during brainstorming).

## Design

### 1. API — `IMemberOptions<TSource, TDest, TMember>`

Add two new overloads mirroring the current ones, differing only in the
lambda return type generic parameter:

```csharp
public interface IMemberOptions<TSource, TDest, TMember>
{
    // existing
    void MapFrom(Expression<Func<TSource, TMember>> sourceExpression);
    void MapFrom<TParam>(ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TMember>> sourceExpression);

    // new
    void MapFrom<TSourceMember>(
        Expression<Func<TSource, TSourceMember>> sourceExpression);

    void MapFrom<TSourceMember, TParam>(ParameterSlot<TParam> parameter,
        Expression<Func<TSource, TParam, TSourceMember>> sourceExpression);
}
```

When `TSourceMember == TMember` C# overload resolution picks the existing
non-generic overload (exact match wins). For collections or convertible
scalars the user gets the new overload.

### 2. `MemberMapBuilder` implementation

Two additional methods that write into the same `SourceExpression` /
`ParameterizedSourceExpression` fields. `PropertyMap` already stores
`LambdaExpression` without a type parameter, so no schema change is needed.

### 3. `ProjectionCompiler` — fail-fast mode

At `ProjectionCompiler.cs:95-136` the compiler already handles the branch
where `SourceExpression` is set from an explicit user `MapFrom`. The existing
mismatch path calls `TryBuildNestedCollection`; if it returns `false` *and*
the container types are only compatible at the collection-shape level, the
compiler either issues `Expression.Convert` (compatible elements) or
`continue`-skips (incompatible elements).

Change: when the expression came from an **explicit** `MapFrom` and
`TryBuildNestedCollection` returns `false` and `Expression.Convert` is not
applicable, throw `InvalidOperationException` with a message naming the
source/destination element types and the member. Convention-path behaviour
is preserved — it keeps silent-skip.

Tracking: introduce a local `bool isExplicit` set in the `SourceExpression`
branch and checked before the skip.

### 4. Runtime Mapper (`IMapper.Map`)

No direct change. `MapperConfiguration` at `MapperConfiguration.cs:92` builds
the runtime delegate by compiling the same expression `ProjectionCompiler`
produced. Fixing `ProjectionCompiler` fixes both paths.

### 5. Tests

**Unit**

- `MemberMapBuilderTests` — new overloads populate `SourceExpression` /
  `ParameterSlot` correctly.
- `ProjectionCompilerTests`
  - explicit collection `MapFrom` with registered element `TypeMap` builds
    a `Select` projection.
  - explicit collection `MapFrom` without element `TypeMap` throws
    `InvalidOperationException` containing the element type names.
  - convention-path collection with missing element `TypeMap` still silently
    skips (regression guard).
  - parameterized explicit collection `MapFrom` compiles and propagates
    parameter slot.
- `MapperTests` — runtime `Map()` over a root with nested children produces
  the correct structure via explicit `MapFrom`.

**Integration (`tests/MyAutoMapper.IntegrationTests/EfCore`)**

- EF Core projection test that uses a profile with explicit
  `ForMember(d => d.Children, o => o.MapFrom(s => s.Children))` (no
  convention fallback); `MaxDepth` respected.

### 6. Sample update

`samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs`:
replace the "convention" comment with the explicit mapping. The file becomes
the worked example for the new API.

```csharp
CreateMap<Category, CategoryViewModel>()
    .MaxDepth(5)
    .ForMember(d => d.LocalizedName, o => o.MapFrom(lang,
        (src, l) => l == "uz" ? src.NameUz
                  : l == "lt" ? src.NameLt
                  : src.NameRu))
    .ForMember(d => d.Children, o => o.MapFrom(src => src.Children));
```

## Affected Files

- `src/MyAutoMapper/Configuration/IMemberOptions.cs`
- `src/MyAutoMapper/Configuration/MemberMapBuilder.cs`
- `src/MyAutoMapper/Compilation/ProjectionCompiler.cs`
- `tests/MyAutoMapper.UnitTests/Configuration/MemberMapBuilderTests.cs`
  (new)
- `tests/MyAutoMapper.UnitTests/Compilation/ProjectionCompilerTests.cs`
- `tests/MyAutoMapper.UnitTests/Runtime/MapperTests.cs`
- `tests/MyAutoMapper.IntegrationTests/EfCore/ExplicitCollectionMapFromTests.cs`
  (new)
- `samples/MyAutoMapper.WebApiSample/Profiles/CategoryViewModelProfile.cs`

## Risks

- Overload resolution ambiguity: verified — exact-match rule picks the
  legacy overload for identical types, eliminating source-compat concerns.
- Error path coverage: the new `throw` must not trigger in convention
  scenarios; dedicated regression test is required.
- Parameterized overload: the new `MapFrom<TSourceMember, TParam>` has four
  generic parameters; documentation must show a usage example to keep it
  discoverable.

## Acceptance Criteria

- Full build succeeds (`dotnet build`).
- All existing tests pass unchanged.
- New tests listed in section 5 pass.
- `CategoryViewModelProfile.cs` uses the explicit `ForMember(...MapFrom...)`
  form and the WebApiSample produces the same HTTP response as before.
