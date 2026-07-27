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


### CornerPoints

Output points at the knots of each spline.

## Attributes

Each point on the Results output gets the attribute row of the spline that it comes from. The node then adds five more attributes.

* `splineIndex` — index of the source spline in the flattened input order.
* `splineT` — normalized position along the spline, from 0 to 1.
* `splineDistance` — distance along the spline, in world units.
* `splineWidth` — width of the spline at that position, from the `pcg.width` channel.
* `splineSide` — `+1` or `-1` if BothSides is true. In all other conditions the value is `0`.

Each point on the CornerPoints output gets the attribute row of its spline and the `splineIndex` attribute only.

Use `splineSide` to put different objects on the two sides of a road. Use `splineWidth` to change the object or its scale on a wide road.
