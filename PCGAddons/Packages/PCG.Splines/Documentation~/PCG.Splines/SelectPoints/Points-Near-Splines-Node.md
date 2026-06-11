# PointsNearSplinesNode

Separates input points into two sets based on proximity to splines.
Points within the specified distance of any spline go to NearPoints; others go to Results.

## Inputs

### Distance

Distance threshold for proximity test.

### Points

Input points to test for proximity.

### Splines

Splines to measure distance against.

## Variables

### Mode

Proximity mode. ThreeD measures distance in 3D; TwoD ignores the Y axis and measures distance in the XZ plane only.

### UseScale

If true, scales the distance threshold per point using its Scale.

## Outputs

### NearPoints

Points near any spline (distance less than or equal to threshold).

### Results

Points far from all splines (distance greater than threshold).

