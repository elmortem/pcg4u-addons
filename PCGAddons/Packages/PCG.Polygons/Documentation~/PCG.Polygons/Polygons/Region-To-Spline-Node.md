# RegionToSplineNode

Converts regions into closed splines.
Each region produces one closed spline for its outer contour and one closed spline per hole, placed at the region set PlaneY.

## Inputs

### Region

Region set to convert.

## Outputs

### Splines

Closed splines for every contour and hole of the input regions.

## Attributes

This node is the bridge from region attributes to spline attributes. Each output spline gets the attribute row of its source region. The outer contour and all holes of one region get the same row.

The node also writes the `regionIndex` attribute. The value is the index of the source region.

Thus the city attributes `lotId`, `depth`, `cutDepth` and `boundary` stay available on the splines and on all points that come from them.
