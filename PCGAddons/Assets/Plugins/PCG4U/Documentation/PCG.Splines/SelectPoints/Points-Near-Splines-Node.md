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

## Outputs

### NearPoints

Points near any spline (distance less than or equal to threshold).

### Results

Points far from all splines (distance greater than threshold).

