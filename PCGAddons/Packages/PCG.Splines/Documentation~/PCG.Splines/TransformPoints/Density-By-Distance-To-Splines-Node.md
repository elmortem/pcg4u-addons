# DensityByDistanceToSplinesNode

Changes point density based on the distance to the nearest spline, mapped through a curve.
Distance is normalized by Radius (0 at a spline, 1 at or beyond Radius) before being evaluated.

## Inputs

### Points

The input list of points to process.

### Splines

The splines distances are measured to. With no splines, every point uses the curve value at distance 1.

### Radius

The distance over which the curve is evaluated.

## Variables

### Curve

Maps the normalized distance to a density value.

### Mode

How the curve value is applied to existing density: Add, Mult or Set.

## Outputs

### Results

The processed list of points with updated density values.
