# Changelog

## 1.1.0 — 2026-04-19

### Added
- New `ProjectTo<TDest>(IQueryable, IProjectionProvider)` and `ProjectTo<TDest>(IQueryable, IProjectionProvider, Action<IParameterBinder>)` overloads for explicit DI
- AOT/trimming markers (`[RequiresDynamicCode]`, `[RequiresUnreferencedCode]`) on all public entry points
- SourceLink + deterministic builds + symbol packages (`.snupkg`)
- IL-compiled `Factory` and per-property `Setters` delegates for closure holders (perf)
- `ProjectionBenchmark` in `tests/SmAutoMapper.Benchmarks` for ongoing perf regression tracking

### Changed
- Migrated project and directory identity from `MyAutoMapper` to `SmAutoMapper` (matches the published NuGet package name)
- `AddMapping` no longer calls `services.BuildServiceProvider()` (fixes ASP0000)
- CI migrated to working `ci.yml` on push/PR to master

### Deprecated
- `ProjectionProviderAccessor` — inject `IProjectionProvider` via DI instead (diagnostic `SMAM0001`)
- Accessor-based `ProjectTo<TDest>(IQueryable)`, `ProjectTo<TDest>(IQueryable, Action<IParameterBinder>)`, `ProjectTo<TSource, TDest>(IQueryable<TSource>)`, `ProjectTo<TSource, TDest>(IQueryable<TSource>, Action<IParameterBinder>)` overloads — use the overloads that take `IProjectionProvider` (`SMAM0002`)

### Removed
- Committed `.nupkg` artifacts from `nupkg/` and `src/MyAutoMapper/nupkg/`
- Broken `.github/workflows/dotnet.yml`

### Performance
- Projection hot path no longer calls `Activator.CreateInstance` or `PropertyInfo.SetValue` — uses IL-compiled delegates instead. See `ProjectionBenchmark` results in PR #4.
