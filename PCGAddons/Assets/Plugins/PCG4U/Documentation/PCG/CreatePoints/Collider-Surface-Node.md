# ColliderSurfaceNode

Generates points on physics colliders by casting rays through a box-shaped volume in the given direction.
Rays start outside the volume and hit the active editor physics scene; only hits inside the box are kept.

## Inputs

### CellSize

The cell size used to space points when a regular mode is selected.

### Count

The number of rays cast in random modes.

### Direction

The direction rays travel toward the colliders.

### Offset

The center of the sampling box in world space.

### PoissonMinDistance

The minimum distance between points used by the `Poisson` mode. Larger values yield sparser, more evenly spaced points and can reduce the final count below `Count`.

### Seed

The seed for random number generation. `-1` picks a random seed on bind.

### Size

The size of the sampling box.

## Variables

### Layers

The physics layers the rays can hit.

### PointMode

The mode for distributing the rays (surface/volume, random/regular). In `Poisson` mode the rays are cast with the random distribution and the resulting hits are thinned so that no two points are closer than `PoissonMinDistance`.

## Outputs

### Results

The list of points placed on the hit colliders, available as an output.

