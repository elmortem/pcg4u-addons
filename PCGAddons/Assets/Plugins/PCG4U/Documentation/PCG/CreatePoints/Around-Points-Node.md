# AroundPointsNode

Generates new points around existing input points within a spherical distribution.
This node creates a specified number of new points around each input point,
with customizable radius range and axis multipliers to control the distribution shape.

## Inputs

### AxesMult

The multiplier for each axis (x, y, z) to control the shape of the point distribution.
Use (1,1,1) for spherical distribution, (1,0,1) for planar, etc.

### Count

The minimum and maximum number of new points (x = min, y = max) to generate around each input point.

### Points

The input list of points around which new points will be generated.

### Radius

The minimum and maximum radius (x = min, y = max) within which new points will be generated.

### Seed

The seed for the random number generator.

## Outputs

### Results

The list of generated points, available as an output.

