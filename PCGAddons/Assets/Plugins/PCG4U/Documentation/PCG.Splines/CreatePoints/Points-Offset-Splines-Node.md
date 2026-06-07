# PointsOffsetSplinesNode

Generates points along input splines, optionally offset to one or both sides.
Spacing is measured by arc length, so the step stays uniform on straight and curved sections alike.
A second output emits one point per spline knot (corner), passed through the same offset and orientation.

## Inputs

### Splines

The input list of splines to sample along.
Splines with one knot or less are skipped.

### Offset

Offset magnitude from the spline, applied along the path normal.
Set to 0 to place points directly on the spline (in that case BothSides is ignored and a single point is emitted).

### Distance

Sampling step along the spline, in world units (arc length).
Used by the Distance and Fit spacing modes.

### Count

Number of points to emit when Spacing is set to Count.

## Variables

### Spacing

Spacing mode that controls how points are distributed along the spline:
* **Distance** - points at 0, Distance, 2×Distance, ... until the spline length is exceeded.
* **Count** - exactly Count points; on an open spline evenly from start to end inclusive, on a closed spline without a duplicate at the seam.
* **Fit** - the step is rounded to fit the spline length evenly (steps = round(length / Distance)); on an open spline points run exactly from start to end.

### BothSides

If true, emits points on both sides of the spline. Ignored when Offset is 0.

### UpNormal

If true, uses Vector3.up as the point normal instead of the spline up vector.

### NoRotation

If true, the point angle is forced to 0 instead of the LookRotation-based yaw.

## Outputs

### Results

The list of generated points sampled along the splines.

### CornerPoints

One point per spline knot (corner), passed through the same offset and orientation as Results.
Useful for placing items at structural corners (for example fence posts at section joints).
