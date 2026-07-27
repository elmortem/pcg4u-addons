# SplinesSurfaceNode

Generates points on/along input splines using a selectable generation mode.
Supports offset and deterministic seeding.

## Inputs

### Count

Number of points to generate per spline.

#### Remarks
Number of points to generate per spline.

### Offset

World-space offset applied to generated points.

#### Remarks
World-space offset applied to generated points.

### Seed

Seed for deterministic generation. Use 0 (or any value ≤ 0) for non-deterministic behavior.

#### Remarks
Seed for deterministic generation. Use 0 (or any value ≤ 0) for non-deterministic behavior.

### Splines

Input splines to generate points from.

#### Remarks
Input splines to generate points from.

## Variables

### PointMode

Generation mode for sampling points along splines. In `SurfaceRegular` mode the points are spaced evenly by arc length, so the real distance between them stays constant even on curved segments.

## Outputs

### Results

Output generated points.

#### Remarks
Output generated points.

## Attributes

Each point gets the attribute row of the spline that it comes from. The node then adds the position of the point on that spline.

* `splineIndex` — index of the source spline in the flattened input order.
* `splineT` — normalized position along the spline, from 0 to 1.
* `splineDistance` — distance along the spline, in world units.
* `splineWidth` — width of the spline at that position, from the `pcg.width` channel.

In the two volume modes a point is inside the contour and not on the curve. Thus `splineT` and `splineDistance` have the value `-1`, and `splineWidth` has the value `0`.
