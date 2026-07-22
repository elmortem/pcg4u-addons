## Description
Addon that sweeps a 2D cross-section profile along Unity Splines and builds meshes: roads, paths, walls, channels. UV runs by distance along the spline, width/height are driven by curves, the profile can twist, ends can be capped, and the strip can be draped over a terrain.

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
* **Custom** — arbitrary open or closed contour. Non-finite and coincident points are dropped; fewer than two valid points falls back to Ribbon. A closed contour is normalized to keep outward normals regardless of the input winding and gets a duplicated UV seam vertex.

## Sweep Spline

Sweeps the profile along every input spline and outputs `MeshInstanceData` materialized by the core mesh instance maker. The node is self-contained: connect splines and it builds immediately using the inline profile; connect the `Profile` port to reuse a `Profile` node.

Fields:
* **Enabled** — whether the node produces mesh instances.
* **Splines** — splines the profile is swept along (multi-input).
* **Topology** — optional `SplineNetworkTopology` from a `Spline Intersection` node. Connected, it turns the node into a road network: the splines are split at the junctions, each piece is swept with a setback from the crossing, and every intersection is closed by a junction patch. Left empty, the node behaves exactly like before and sweeps the whole splines with no patches.
* **Profile** — optional profile override; inline profile fields are used when it is not connected.
* **Shape / Width / Height / Custom Points / Custom Closed** — inline profile, same meaning as the `Profile` node.
* **Step** — minimum length of a sweep segment and the placement quantum (minimum 0.05). Rings are only placed on multiples of this distance, never finer, so the vertex count is predictable at `ceil(length / Step) + 1` rings. Open splines get at least two rings, closed splines at least three plus a seam ring so the longitudinal UV does not wrap back. In network mode it is also the ring spacing of the junction patches.
* **Max Step** — the ceiling for thinning out rings on straight sections (sanitized up to `Step` when set smaller). Keeps the strip hugging the terrain when draping.
* **Max Angle** — accumulated tangent turn in degrees (clamped 0.5..180) after which the next ring must be emitted. Straight sections stretch to `Max Step`, arcs get one ring per this much turn.
* **Width By T / Height By T** — profile X/Y multipliers by normalized spline length; clamped to a small positive value so a taper to zero does not create degenerate triangles.
* **Twist By T** — profile rotation in degrees around the tangent by normalized spline length. Order per vertex: X/Y scale → twist → placement in the spline frame.
* **Cap Ends** — caps the ends of a closed profile on an open spline (ear-clipped, hard-edged, front cap faces against the first tangent, back cap along the last tangent). In network mode only the free ends of a piece are capped, never the ends that meet a junction.
* **Setback Scale** — network mode only. Multiplies the automatic per-arm setback that pulls each piece back from a junction centre before the patch takes over; 1 uses the mitre-derived setback, larger opens the intersection wider.
* **Uv Scale** — longitudinal UV scale (V = distance × Uv Scale); U runs across the profile 0..1. In network mode the strips keep this longitudinal UV; the junction patch has its own planar unwrap.
* **Terrain** — terrain the strip is draped over; empty keeps vertices in the spline frame. When assigned, XZ follows the projected right axis and Y samples the terrain (bilinear) plus **Height Offset**; a part outside the terrain keeps the spline height and logs one warning.
* **Terrain Offset** — world-space position of the terrain.
* **Height Offset** — vertical offset above the terrain surface.
* **Name** — name of the created mesh objects (a per-mesh index is appended when several are built; in network mode each junction patch is its own object named `Name Junction i`).
* **Material** — material assigned to the mesh (and to the strip meshes in network mode).
* **Junction Material** — network mode only; material assigned to the junction patches. Falls back to **Material** when empty.
* **Collider** — adds a `MeshCollider` to the created objects.

### Behavior

* Geometry is built off the main thread from a full immutable snapshot (profile arrays, per-spline frames, curve LUTs, terrain height window). Invalid splines (null, single knot, zero length, non-finite frames) are skipped; results keep the input order.
* Folds are removed by self-intersection of the profile's offset column polylines: each column of the profile forms a polyline along the spline, a fold is a loop of that polyline, and the rings inside the loop snap to the intersection point, so the inner edges meet there and continue as the fan seam with continuous coverage. Overlaps without a fold (crossings of a self-intersecting spline, bridges of the spline over itself, arms overlapping far from the fold) are left untouched; degenerate triangles and duplicate vertices are then removed.
* Draping snapshots only the terrain height window that covers the sweep bounds instead of the whole heightmap.
* Editing a spline, profile, curve or terrain during a compute cancels and recomputes; the scene is synchronized by a single finalize path, so empty, invalid, disabled and cancelled cases never leave stale or partial objects.
* Terrain edits invalidate the node (heightmap content version is mixed into the node version), so Auto Generate rebuilds without touching a parameter.

### Network mode (Topology connected)

* The topology is used exactly as authored — Sweep never removes cuts or merges junctions, and the network semantics never depend on `Step` or the profile size. Clustering of crossings is the single responsibility of the upstream `Spline Intersection` node and its **Merge Distance** handle: a closed spline's seam is deduplicated with a circular metric so the two ends of the loop collapse to one incident cut, while a genuine self-crossing far along the loop stays two cuts. The end of a piece knows which junction it belongs to because the split carries that provenance through, so no geometric guessing is needed.
* The splines are split at the cuts with the same solver as `Split Splines`; each piece attaches to a junction at a cut end (from the carried provenance) and stays free at a dangling end (a free end within the profile's lateral extent of a junction also attaches). Width/height/twist curves are sampled by the global normalized position along the original spline, so `Width/Height/Twist By T` stay continuous across every cut.
* Each junction sets a per-arm setback from the mitre of the angular gaps to its neighbours, capped at 2.5× the arm half-width so sharp corners do not blow the patch up. On very sharp intersections the arms still overlap: the plate domains are clipped by the intersection point of neighbouring arm end chords, so each outline stays simple, and the overlapping arm strips may briefly overlap in height near the crossing — a compromise tunable with **Setback Scale** (larger opens the intersection wider).
* A piece left shorter than the placement quantum `Step` after both setbacks is **absorbed**: no strip is built, its range collapses to a single distance and the junction plates on both sides meet ring-to-ring at that point. A stab — a piece shorter than the setback with a free end — is absorbed to its free end instead: the arm keeps a terminal frame at the far end of the piece and, with **Cap Ends**, that end is closed by a profile cap so the short spur reads as a solid capped plate rather than a dangling strip. A piece whose two ends land in the same junction closer than twice the lateral extent is collapsed to a single arm so the seam does not double up.
* The profile is decomposed into **leaf chains** (maximal runs where the lateral axis is monotone — each chain is a graph `y = f(x)` over the width) and **walls** (vertical runs). A ribbon is one chain; a rectangle is an upper chain, a lower chain and two walls; a trapezoid is an upper and a lower chain with the slanted sides folded into them, so a non-vertical silhouette is carried through the patch instead of being lost.
* Each junction is closed by one **plate patch** built per chain and per wall. For a chain the plate domain is a single simple planar polygon — the arms' chain rings joined by quadratic-Bézier **rims** (fillets) between neighbouring outer corners. The domain is filled with a graded interior point grid and a Delaunay triangulation constrained to the domain (no fan triangulation), giving near-equilateral triangles; the arm ring vertices stay on the boundary bit-exact, so the strip and patch meet without a seam. Interior heights and the terrain drape offset are interpolated across the domain with **mean value coordinates** (Hormann–Floater), which reproduce the boundary exactly at the rings, stay smooth inside, and keep a flat crossing flat and a uniformly sloped crossing planar. Walls are ruled bands lofted along the rims of their bottom and top points; terminal stabs are closed with a profile cap when **Cap Ends** is on.
* The sheets use a planar unwrap in the junction frame (U/V are the in-plane offset from the centre); the bands run the profile U across the perimeter and V along the rim length, so the walls keep the profile texture and the sheets carry no radial stretch.
* The decomposition is done in profile space; a torso twist of `|twist| >= 90°` at an arm end collapses the chains in plan and is reported as a per-patch failure (see below) rather than silently corrupting the mesh.
* Without terrain the interior grid follows `max(Step, junction radius / 24)`; with terrain the same grid heights are draped onto the surface through the mean-value offsets so the plate hugs the terrain.
* A junction patch that cannot be built is skipped on its own: the strips and the other patches still publish, one diagnostic names the node, the junction index and a code (`DegenerateChains`, `DomainNotSimple`, `TriangulationFailed`, `PortalSeamMismatch`, `BudgetExceeded`), and one summary warning reports how many patches failed. A patch whose upper vertex estimate exceeds two million is rejected as `BudgetExceeded` before it allocates.
* Strips carry **Material**, patches carry **Junction Material** (or **Material** when empty); each junction is its own object named `Name Junction i` and materialized by the core mesh instance maker.
* Topology content version is mixed into the node version, so editing the network rebuilds the node. The old separate `Sweep Network` node was merged into this node — graphs that still reference it show a missing node and should replace it with `Sweep Spline` with **Topology** connected.

## Presets

`Presets/StonePath.asset` is a ready-to-use `PcgSubGraph` for a stone footpath. Drop a `Sub Graph` node referencing it onto a `PcgComponent`, connect a `Spline` node into the `Splines` port, and fill the blackboard pills.

* **Inputs (pills):** `Splines`, `Terrain`, `PathMaterial`, `Stones` (GameObject weights), `Width`, `StoneOffset`, `Seed`.
* **Outputs:** `Path` (the swept ribbon mesh, collider on) and `Stones` (prefab instances on both verges).
* **Required makers:** the host object must carry **both** `GameObjectInstanceMaker` (stones) and `MeshInstanceMaker` (path mesh) in `Instance Maker Components` — without the mesh maker the path is not materialized.
* **Invariant:** `StoneOffset >= Width / 2 + largest stone radius` so stones never sit on the path.
* **Package dependency:** the preset uses `SplinesValue` and `Points Offset Splines`, so `com.elmortem.pcg.splines` is declared in `package.json`.
