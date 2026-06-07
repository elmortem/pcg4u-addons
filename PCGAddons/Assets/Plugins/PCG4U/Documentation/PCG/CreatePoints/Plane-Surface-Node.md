# PlaneSurfaceNode

Represents a node for generating points on a plane surface in a procedural content generation system.
This node allows for the creation of points with various distribution modes and parameters.

## Inputs

### Count

The number of points to generate on the plane.

### Offset

The offset of the plane from the origin.

### PoissonMinDistance

The minimum distance between points used by the `Poisson` mode. Larger values yield sparser, more evenly spaced points and can reduce the final count below `Count`.

### Seed

The seed for random number generation.

### Size

The size of the plane (width and length).

## Variables

### PointMode

The mode for generating points on the plane surface.

#### Remarks
Available modes:
- `GeneratePointMode.None`: No points are generated.
- `GeneratePointMode.SurfaceRandom`: Points are randomly distributed across the plane surface.
- `GeneratePointMode.SurfaceRegular`: Points are evenly distributed in a grid pattern across the plane surface.
- `GeneratePointMode.VolumeRandom`: Not applicable for a plane surface. Defaults to SurfaceRegular with a warning.
- `GeneratePointMode.VolumeRegular`: Not applicable for a plane surface. Defaults to SurfaceRegular with a warning.
- `GeneratePointMode.Poisson`: Evenly scattered points without clumping. The surface is oversampled with the random distribution and thinned so that no two points are closer than `PoissonMinDistance`.

## Outputs

### Results

The list of generated points, available as an output.

