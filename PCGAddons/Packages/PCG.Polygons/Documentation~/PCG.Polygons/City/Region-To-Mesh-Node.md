# RegionToMeshNode

Builds a clean, crack-free mesh of the merged regions, draped over a terrain with tessellation that follows the relief.

All regions are merged into one polygon set (overlaps removed, holes kept). With a terrain, a world-aligned restricted quadtree is built over the bounds: cells split while the terrain deviates from a bilinear approximation of their corners by more than MaxHeightError, down to MinCellSize / MaxDepth. Interior leaves are triangulated with transition fans (no T-junctions), boundary leaves are clipped against the polygon and triangulated. Every vertex is lifted to the terrain height plus HeightOffset. Without a terrain (or MaxCellSize <= 0), the merged polygon is triangulated once on the PlaneY plane. The result is emitted as mesh instance data and materialized through the host instance maker while generating or previewing.

## Inputs

### Region

Region set to mesh.

### Terrain

Terrain data used to sample heights and drive tessellation. If empty, the mesh is flat on PlaneY.

### Offset

World offset of the terrain origin, applied when sampling heights.

### MaxHeightError

Maximum allowed deviation of the terrain from the bilinear cell approximation before a cell is split. Smaller values follow the relief more closely with more triangles.

### MinCellSize

Smallest cell edge length. Bounds the finest tessellation and the boundary cell size.

### MaxCellSize

Largest cell edge length (quadtree root cell). Set to 0 or below to skip tessellation and emit a single flat triangulation.

### MaxDepth

Maximum quadtree subdivision depth, a hard cap on tessellation alongside MinCellSize.

### HeightOffset

Height added above the terrain surface to avoid z-fighting.

### UvScale

Scale of the generated UVs in world units.

### Name

Name of the produced mesh instance.

## Variables

### Enabled

If false, the node produces nothing and clears any instances it created.

### Material

Material assigned to the produced mesh.

## Outputs

### Results

The mesh instance data for the meshed regions.
