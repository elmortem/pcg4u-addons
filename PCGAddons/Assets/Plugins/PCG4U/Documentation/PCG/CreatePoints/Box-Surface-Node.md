# BoxSurfaceNode

This class represents a node that generates points on a box surface based on specified parameters such as offset, size, count, and seed. It implements asynchronous preview functionality and provides the ability to draw a preview of the generated points in the scene. The class also includes methods to compute the points based on the input parameters and handle the generation process using the internal organization's PCG module.

## Inputs

### Count

The number of points to generate on the box.

### Offset

The offset of the box from the origin.

### PoissonMinDistance

The minimum distance between points used by the `Poisson` mode. Larger values yield sparser, more evenly spaced points and can reduce the final count below `Count`.

### Seed

The seed for random number generation.

### Size

The size of the box.

## Variables

### PointMode

The mode for generating points on the box surface. In `Poisson` mode points are scattered evenly over the surface without clumping: the surface is oversampled with the random distribution and thinned so that no two points are closer than `PoissonMinDistance`.

## Outputs

### Results

The list of generated points, available as an output.

