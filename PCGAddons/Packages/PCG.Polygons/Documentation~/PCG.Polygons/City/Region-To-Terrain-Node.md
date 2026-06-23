# RegionToTerrainNode

Drapes each region as a mesh over a terrain.
The region is triangulated, its edges are subdivided where they are longer than MaxEdgeLength (up to MaxSubdivisions), and every vertex is lifted to the terrain height plus HeightOffset. The result is emitted as mesh instance data and materialized through the host instance maker while generating or previewing.

## Inputs

### Region

Region set to drape.

### Terrain

Terrain data used to sample heights.

### Offset

World offset of the terrain origin, applied when sampling heights.

### MaxEdgeLength

Edges longer than this are subdivided so the mesh follows the terrain.

### MaxSubdivisions

Maximum number of subdivisions applied to an edge.

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

The mesh instance data for the draped regions.
