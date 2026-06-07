# DensityByDistanceNode

Changes point density based on the distance to the nearest target point, mapped through a curve.
Distance is normalized by Radius (0 at a target, 1 at or beyond Radius) before being evaluated.

## Inputs

### Points

The input list(s) of points to process.

### Radius

The distance over which the curve is evaluated.

### TargetPoints

The points distances are measured to. With no targets, every point uses the curve value at distance 1.

## Variables

### Curve

Maps the normalized distance to a density value.

### Mode

How the curve value is applied to existing density: Add, Mult or Set.

## Outputs

### Results

The processed list of points with updated density values.

