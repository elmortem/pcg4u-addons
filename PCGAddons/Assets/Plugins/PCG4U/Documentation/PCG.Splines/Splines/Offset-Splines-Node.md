# OffsetSplinesNode

Shifts each spline knot sideways by a fixed offset, perpendicular to the spline tangent.
A positive Offset moves to one side, a negative Offset to the other - useful for road shoulders or parallel paths.
This moves knots rather than building a true parallel curve; for sparse knots run the spline through Resample Splines first.

## Inputs

### Splines

The input list of splines to offset.
Splines with one knot or less are skipped.

### Offset

Sideways offset distance, in world units. Negative values offset to the opposite side.

### Up

Up vector used together with the spline tangent to compute the sideways direction.

## Outputs

### Results

The list of offset splines, preserving the Closed state of each input.
