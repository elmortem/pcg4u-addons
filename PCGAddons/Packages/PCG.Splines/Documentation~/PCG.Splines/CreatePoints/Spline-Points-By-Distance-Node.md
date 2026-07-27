# SplinePointsByDistanceNode

Generates points along input splines spaced by a fixed distance along the arc length.

## Inputs

### Splines

Input splines to generate points from.

### Distance

Target spacing between points, in meters along the spline.

## Variables

### Distribute

When enabled, the step is adjusted so that points fit the spline exactly: on an open spline the first and last points land on the ends, on a closed spline points are distributed around the loop without a duplicate at the seam. When disabled, a fixed step of `Distance` is used from the start and the remainder is cut.

## Outputs

### Results

The list of generated points.

## Attributes

Each point gets the attribute row of the spline that it comes from. The node then adds the position of the point on that spline.

* `splineIndex` — index of the source spline in the flattened input order.
* `splineT` — normalized position along the spline, from 0 to 1.
* `splineDistance` — distance along the spline, in world units.
* `splineWidth` — width of the spline at that position, from the `pcg.width` channel.
