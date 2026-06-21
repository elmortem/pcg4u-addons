# Clipper2 (vendored)

Polygon clipping/offsetting backend used by `PolygonClipper`.

- Upstream: https://github.com/AngusJohnson/Clipper2 (`CSharp/Clipper2Lib/`, branch `main`, 2026-04-20)
- Namespace: `Clipper2Lib` (the `#if USINGZ` branches compile to `Clipper2Lib` since `USINGZ` is not defined)
- License: Boost Software License 1.0 (see `LICENSE`)
- Part of the `PCG.Polygons` assembly (no separate asmdef)

## Vendored files

- `Clipper.cs`, `Clipper.Core.cs`, `Clipper.Engine.cs`, `Clipper.Offset.cs`
- `Clipper.RectClip.cs`, `Clipper.Minkowski.cs`, `Clipper.Triangulation.cs`
- `HashCode.cs` (`Clipper2Lib.HashCode`), `PooledList.cs` (pooled lists used by the engine)

`Clipper2Lib.csproj` / `Clipper2.snk` are not vendored.

## Local change vs upstream

Upstream relies on `<Nullable>enable</Nullable>` from its `.csproj`. Unity has no
project-wide nullable context, so `#nullable enable` was added at the top of the four
files that use `?` annotations without already declaring it: `Clipper.Offset.cs`,
`Clipper.Triangulation.cs`, `HashCode.cs`, `PooledList.cs`. The other five files
already carry `#nullable enable` upstream and are verbatim. Re-apply this when updating.
