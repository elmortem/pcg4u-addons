# PointToTerrainNode

Projects input points onto a terrain surface, updating their positions and normals.
Supports different projection modes (surface, raycast, etc.).

## Inputs

### Offset

World offset of the terrain transform.

### Points

Input points to be projected onto the terrain.

### Terrain

Target terrain data to project points onto.

## Variables

### ProjectionMode

Method used for projecting points onto the terrain.

### ProjectNormal

When enabled, each projected point gets the interpolated terrain normal. When disabled, the original point normal is preserved. Enabled by default.

## Outputs

### Results

Output points projected onto the terrain.

