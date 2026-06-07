# SplineFromPointsNode

Builds a spline from a list of points.
Each input list of points produces a separate spline, with knots following the order of the points in the list.
Lists with one point or less are skipped.

## Inputs

### Points

The input list of points to turn into spline knots.
Knot positions follow the point positions in list order.

## Variables

### Closed

If true, the generated spline is closed (the last knot connects back to the first).

## Outputs

### Results

The list of generated splines, one per input point list.
