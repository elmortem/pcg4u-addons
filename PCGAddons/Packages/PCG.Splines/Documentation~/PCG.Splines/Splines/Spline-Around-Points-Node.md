# SplineAroundPointsNode

Generates closed splines around existing input points in a circular distribution.
This node creates a closed spline around each input point with a specified number
of control points, allowing for customizable radius and orientation.

## Inputs

### Points

The input list of points around which splines will be generated.

### PointsCount

The number of control points to create for each spline.
Higher values result in smoother, more detailed splines.
Minimum recommended value is 3.

### Radius

The minimum and maximum radius (x = min, y = max) that determines the size of generated splines.
Each control point of the spline will be placed at a random distance within this range from the center point.

### Seed

The seed for the random number generator.
Using the same seed will produce the same spline shapes.
Set to -1 for random seed on startup.

### Up

The up vector that determines the orientation of the generated splines.
The splines will be generated in a plane perpendicular to this vector.

## Outputs

### Results

The list of generated splines, available as an output.
Each spline is closed and has the specified number of control points.

