# DensityByDistanceToSplinesNode

Adjusts point density based on the distance from each point to the nearest spline.
Splines are sampled into points, the distance to the closest sample is normalized by Radius, and the result is fed through a curve.
The curve value is applied to density via the selected mode and clamped to [0..1].
Combine with Points By Density to keep or drop points near or far from splines.

## Inputs

### Points

The input list of points whose density will be modified.

### Splines

The splines that points are measured against.

### Radius

Distance, in world units, over which the falloff is evaluated.
Points farther than Radius from any spline use the curve value at distance 1.
A radius below the minimum leaves density unchanged.

## Variables

### Curve

Falloff curve evaluated with a normalized distance in [0..1] (0 = on the spline, 1 = at Radius).
Defaults to a linear falloff from 1 at the spline to 0 at Radius.

### Mode

How the curve value is combined with the existing density:
* **Set** - replaces density with the curve value.
* **Add** - adds the curve value to density.
* **Mult** - multiplies density by the curve value.

## Outputs

### Results

The input points with adjusted density.
