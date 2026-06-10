# RandomSplineNode

Generates random splines between pairs of input points.
This node creates splines by connecting every two consecutive points with a randomized path,
adding intermediate control points with random offsets to create natural-looking curves.

## Inputs

### Height

The minimum and maximum height (x = min, y = max) for random offsets.
Determines how far the spline can deviate from the direct path between points.

### Points

The input list of points to connect with splines.
Points are processed in pairs - every two consecutive points form start and end points of a spline.

### Seed

The seed for the random number generator.
Using the same seed will produce identical spline variations.
Set to 0 (or any value ≤ 0) for a random seed on startup.

### Segments

The number of intermediate segments in each spline.
Higher values create smoother curves with more variation points.

### Up

The up vector that determines the orientation for calculating perpendicular offsets.
Used to determine the direction in which random variations will be applied.

## Outputs

### Results

The list of generated splines, available as an output.
Each spline connects two consecutive input points with random variations.

