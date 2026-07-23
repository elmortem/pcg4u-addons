## Description
Addon that sweeps a 2D cross-section profile along Unity Splines and builds meshes: roads, paths, walls, channels. UV runs by distance along the spline, width/height are driven by curves, the profile can twist, ends can be capped, and the complete result can be shifted along world Y.

## Nodes

### Sweep
* Profile
* Sweep Spline

## Profile

Builds a `SweepProfile` — the 2D cross-section swept along the spline. The profile lives in a plane where X is across the movement (right) and Y is up. Reusable override for `Sweep Spline`; `Sweep Spline` also has the same inline fields, so a separate `Profile` node is optional.

Fields:
* **Shape** — profile shape.
* **Width** — profile width across the sweep direction.
* **Height** — profile height (Rectangle and HalfPipe).
* **Custom Points** — points of the Custom profile in profile space.
* **Custom Closed** — whether the Custom profile is a closed contour.

### Profile shapes

* **Ribbon** — flat strip, two points, normal points up. Base shape for roads and paths.
* **Rectangle** — closed box outline with hard edges and outward normals (bottom, right, top, left). Walls and rectangular tubes. A perimeter UV seam keeps the texture from stretching on the closing side.
* **HalfPipe** — smooth downward channel (9 shared vertices) for riverbeds and gutters.
* **Pipe** — closed circular profile controlled by **Sides**.
* **Custom** — arbitrary open or closed contour. Non-finite and coincident points are dropped; fewer than two valid points falls back to Ribbon. A closed contour is normalized to keep outward normals regardless of the input winding and gets a duplicated UV seam vertex.

## Sweep Spline

Sweeps the profile along every input spline and outputs `MeshInstanceData` materialized by the core mesh instance maker. The node is self-contained: connect splines and it builds immediately using the inline profile; connect the `Profile` port to reuse a `Profile` node.

Fields:
* **Enabled** — whether the node produces mesh instances.
* **Splines** — splines the profile is swept along (multi-input).
* **Profile** — optional profile override; inline profile fields are used when it is not connected.
* **Shape / Width / Height / Custom Points / Custom Closed** — inline profile, same meaning as the `Profile` node.
* **Sides** — segment count of the inline Pipe profile.
* **Step** — minimum length of a sweep segment and the placement quantum (minimum 0.05). Rings are only placed on multiples of this distance, never finer, so the vertex count is predictable at `ceil(length / Step) + 1` rings. Open splines get at least two rings, closed splines at least three plus a seam ring so the longitudinal UV does not wrap back.
* **Max Step** — the ceiling for thinning out rings on straight sections (sanitized up to `Step` when set smaller).
* **Max Angle** — accumulated tangent turn in degrees (clamped 0.5..180) after which the next ring must be emitted. Straight sections stretch to `Max Step`, arcs get one ring per this much turn.
* **Width By T / Height By T** — profile X/Y multipliers by normalized spline length; clamped to a small positive value so a taper to zero does not create degenerate triangles.
* **Twist By T** — profile rotation in degrees around the tangent by normalized spline length. Order per vertex: X/Y scale → twist → placement in the spline frame.
* **Cap Ends** — caps the ends of a closed profile on an open spline (ear-clipped, hard-edged, front cap faces against the first tangent, back cap along the last tangent).
* **Merge Intersections** — detects overlapping ribbon regions and builds free pieces, corner fans and junction patches.
* **Merge Thickness** — maximum vertical separation at which ribbons are treated as intersecting.
* **Show Intersections / Show All Cuts** — preview diagnostics for merge processing.
* **Uv Scale** — longitudinal UV scale (V = distance × Uv Scale); U runs across the profile 0..1.
* **Height Offset** (`SweepSplineNode.HeightOffset`) — world-Y offset applied to every generated mesh path, independent of profile shape and merge mode.
* **Name** — name of the created mesh objects.
* **Material** — material assigned to the mesh.
* **Junction Material** — material assigned to merge junction patches; falls back to **Material** when empty.
* **Collider** — adds a `MeshCollider` to the created objects.

### Behavior

* Geometry is built off the main thread from a full immutable snapshot (profile arrays, per-spline frames and curve LUTs). Invalid splines (null, single knot, zero length, non-finite frames) are skipped; results keep the input order.
* Folds are removed by self-intersection of the profile's offset column polylines: each column of the profile forms a polyline along the spline, a fold is a loop of that polyline, and the rings inside the loop snap to the intersection point, so the inner edges meet there and continue as the fan seam with continuous coverage. Overlaps without a fold (crossings of a self-intersecting spline, bridges of the spline over itself, arms overlapping far from the fold) are left untouched; degenerate triangles and duplicate vertices are then removed.
* Editing a spline, profile or curve during a compute cancels and recomputes; the scene is synchronized by a single finalize path, so empty, invalid, disabled and cancelled cases never leave stale or partial objects.
* `Sweep Spline` consumes the spline frame as authored and does not read terrain data. Fit a centreline first with `Spline To Terrain` from PCG.Splines when terrain-following placement is required.

## Presets

`Presets/StonePath.asset` is a ready-to-use `PcgSubGraph` for a stone footpath. Drop a `Sub Graph` node referencing it onto a `PcgComponent`, connect a `Spline` node into the `Splines` port, and fill the blackboard pills.

* **Inputs (pills):** `Splines`, `Terrain`, `PathMaterial`, `Stones` (GameObject weights), `Width`, `StoneOffset`, `Seed`.
* **Outputs:** `Path` (the swept ribbon mesh, collider on) and `Stones` (prefab instances on both verges).
* **Path chain:** `Splines → Spline To Terrain` (`Resample`, step 2, normal alignment, height offset 0.08) `→ Sweep Spline`. The projected spline also feeds verge point placement; stone points still use `Point To Terrain` after lateral offset.
* **Required makers:** the host object must carry **both** `GameObjectInstanceMaker` (stones) and `MeshInstanceMaker` (path mesh) in `Instance Maker Components` — without the mesh maker the path is not materialized.
* **Invariant:** `StoneOffset >= Width / 2 + largest stone radius` so stones never sit on the path.
* **Package dependency:** the preset uses `SplinesValue` and `Points Offset Splines`, so `com.elmortem.pcg.splines` is declared in `package.json`.
