# SplineFromPointsNode

Builds a single spline that passes through the input points in their list order.
Each point becomes a knot of the resulting spline.

## Inputs

### Points

The input list of points used as spline knots, in order.

## Variables

### Closed

If true, the spline is closed into a loop by connecting the last knot back to the first.

## Outputs

### Results

The output list containing the created spline.
