# SphereSurfaceNode

Represents a node that generates points on the surface of a sphere based on specified parameters like offset, radius, count, and seed.

## Inputs

### Count

The number of points to generate on the sphere.

### Offset

The offset of the sphere from the origin.

### PoissonMinDistance

The minimum distance between points used by the `Poisson` mode. Larger values yield sparser, more evenly spaced points and can reduce the final count below `Count`.

### Radius

The radius of the sphere.

### Seed

The seed for random number generation.

## Variables

### PointMode

The mode for generating points on the sphere surface. In `Poisson` mode points are scattered evenly over the surface without clumping: the surface is oversampled with the random distribution and thinned so that no two points are closer than `PoissonMinDistance`.

## Outputs

### Results

The list of generated points, available as an output.

