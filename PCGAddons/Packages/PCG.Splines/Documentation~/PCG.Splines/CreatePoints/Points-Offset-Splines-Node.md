# PointsOffsetSplinesNode

Generates points offset from input splines by a given distance along the normal.
Supports both-sides offset, up-normal override and rotation control.

## Inputs

### Distance

Sampling step along splines, in world units.

#### Remarks
Sampling step along splines, in world units.

### Offset

Offset magnitude from the spline.

#### Remarks
Offset magnitude from the spline.

### Splines

Input splines to sample along.

#### Remarks
List of splines to sample along.

## Variables

### BothSides

If true, emits points on both sides of the spline.

#### Remarks
If true, emits points on both sides of the spline.

### NoRotation

If true, angle is forced to 0 instead of LookRotation-based yaw.

#### Remarks
If true, angle is forced to 0 instead of LookRotation-based yaw.

### UpNormal

If true, uses Vector3.up as normal instead of spline up vector.

#### Remarks
If true, uses Vector3.up as normal instead of spline up vector.

## Outputs

### Results

Output list of generated points.

#### Remarks
Output list of generated points.

