## Description
Addon for 2D polygons and regions. A region is a filled polygon (outer contour minus holes) in the XZ plane at the set's PlaneY, with named attributes per region and per edge. Provides conversions to and from Unity splines, boolean operations, and a city pipeline that turns regions into blocks, roads, lots, points and terrain meshes.

City pipeline: Spline To Region -> Subdivide Region (blocks, cut edges tagged with a depth class) -> Assign Road Class By Depth (width per edge by class) -> Blocks To Roads (road ribbons); side branches Inset Region / Lots From Block / Region To Points / Region To Terrain.

## Nodes

### Polygons
* [[Spline To Region Node|PCG.Polygons/Polygons/Spline-To-Region-Node]]
* [[Region To Spline Node|PCG.Polygons/Polygons/Region-To-Spline-Node]]

### City
* [[Subdivide Region Node|PCG.Polygons/City/Subdivide-Region-Node]]
* [[Assign Road Class By Depth Node|PCG.Polygons/City/Assign-Road-Class-By-Depth-Node]]
* [[Blocks To Roads Node|PCG.Polygons/City/Blocks-To-Roads-Node]]
* [[Inset Region Node|PCG.Polygons/City/Inset-Region-Node]]
* [[Lots From Block Node|PCG.Polygons/City/Lots-From-Block-Node]]
* [[Polygon Boolean Node|PCG.Polygons/City/Polygon-Boolean-Node]]
* [[Region To Points Node|PCG.Polygons/City/Region-To-Points-Node]]
* [[Region To Terrain Node|PCG.Polygons/City/Region-To-Terrain-Node]]

### Select Points
* [[Points Near Regions Node|PCG.Polygons/SelectPoints/Points-Near-Regions-Node]]

## Presets

`Presets/CityBlocks.asset` is a ready-to-use `PcgSubGraph` that grows a block city from one closed spline. Drop a `Sub Graph` node referencing it onto a `PcgComponent`, connect a closed `Spline` node into the `Splines` port, and fill the blackboard pills.

* **Inputs (pills):** `Splines` (closed), `Terrain`, `Houses` (GameObject weights), `RoadMaterial`, `Seed`.
* **Outputs:** `Roads` (the road mesh draped on the terrain) and `Houses` (prefab instances placed one per lot, oriented to the nearest road).
* **Required makers:** the host object must carry **both** `GameObjectInstanceMaker` (houses) and `MeshInstanceMaker` (road mesh) in `Instance Maker Components` — without the mesh maker the roads are not materialized.
* **Package dependency:** the preset uses `SplinesValue`, so `com.elmortem.pcg.splines` is declared in `package.json`.
