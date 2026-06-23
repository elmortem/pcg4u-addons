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
