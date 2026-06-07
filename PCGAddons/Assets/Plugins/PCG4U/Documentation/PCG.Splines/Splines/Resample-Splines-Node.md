# ResampleSplinesNode

Rebuilds each input spline with knots evenly spaced by arc length.
The number of steps is rounded so knots fit the spline length evenly; the original shape is preserved while knot density becomes uniform.
Open splines keep knots from start to end inclusive, closed splines avoid a duplicate at the seam.

## Inputs

### Splines

The input list of splines to resample.
Splines with one knot or less are skipped.

### Step

Target distance between knots, in world units (arc length).
The actual step is adjusted to divide the spline length into a whole number of segments.

## Outputs

### Results

The list of resampled splines, preserving the Closed state of each input.
