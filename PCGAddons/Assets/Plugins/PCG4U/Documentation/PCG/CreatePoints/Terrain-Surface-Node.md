# TerrainSurfaceNode

Generates points on a terrain surface based on specified parameters.
This node creates points on a given terrain using various generation modes,
allowing for customization of point distribution, count, and offset.

## Inputs

### Count

The number of points to generate on the terrain surface.

### Offset

The offset applied to the generated points.

### PoissonMinDistance

The minimum distance between points used by the `Poisson` mode. Larger values yield sparser, more evenly spaced points and can reduce the final count below `Count`.

### Seed

The seed for the random number generator.

### ShuffleRegularPoints

Determines whether regular points should be shuffled after generation.

### Terrain

The input terrain data on which points will be generated.

## Variables

### PointMode

The mode used for generating points on the terrain surface. In `Poisson` mode points are scattered evenly over the surface without clumping: the surface is oversampled with the random distribution and thinned so that no two points are closer than `PoissonMinDistance`.

## Outputs

### Results

The list of generated points, available as an output.

