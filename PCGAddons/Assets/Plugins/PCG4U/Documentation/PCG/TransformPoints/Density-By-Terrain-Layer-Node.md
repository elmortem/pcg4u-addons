# DensityByTerrainLayerNode

Changes point density based on the painted weight of a terrain layer in the terrain splatmap.
The alphamap is sampled bilinearly at each point's XZ position; points outside the terrain get value 0.

## Inputs

### Terrain

The TerrainData whose splatmap is sampled.

### Offset

World offset of the terrain origin. The layer weight is sampled at `Position - Offset`.

### Layer

The TerrainLayer to read the weight of. A layer not present in the TerrainData produces an empty result.

### Points

The input list(s) of points to process.

## Variables

### Mode

How the sampled weight is applied to existing density: Add, Mult or Set.

## Outputs

### Results

The processed list of points with updated density values, clamped to [0,1].
