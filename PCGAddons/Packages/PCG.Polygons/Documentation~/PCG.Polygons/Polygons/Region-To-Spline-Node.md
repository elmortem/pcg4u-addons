# RegionToSplineNode

Converts regions into closed splines.
Each region produces one closed spline for its outer contour and one closed spline per hole, placed at the region set PlaneY.

## Inputs

### Region

Region set to convert.

## Outputs

### Splines

Closed splines for every contour and hole of the input regions.
