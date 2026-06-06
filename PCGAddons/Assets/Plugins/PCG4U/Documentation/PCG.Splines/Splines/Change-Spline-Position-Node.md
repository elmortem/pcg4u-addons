# ChangeSplinePositionNode

Changes the positions of spline control points by adding random offsets.
This node modifies each knot position of input splines within specified minimum and maximum ranges.

## Inputs

### Max

The maximum values for the position change in each axis.

### Min

The minimum values for the position change in each axis.

### Seed

The seed for random number generation.
Set to -1 for random seed on startup.

### Splines

The input list of splines to be modified.

## Outputs

### Results

The list of modified splines with updated knot positions.

