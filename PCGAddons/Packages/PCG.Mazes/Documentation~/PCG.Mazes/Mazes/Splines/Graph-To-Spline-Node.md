# GraphToSplineNode

Converts graph edges into Unity splines. Each edge becomes a two-knot spline.

## Inputs

### Graph

Input graph to convert.

## Variables

### AutoSmooth

If true, spline knots use AutoSmooth tangent mode; otherwise Broken mode.

## Outputs

### Splines

Output list of splines (one per graph edge).

## Attributes

The node writes four attributes to each spline.

* `sourceSplineIndex` — index of the graph edge.
* `startJunction` — index of the first node of the edge.
* `endJunction` — index of the second node of the edge.
* `weight` — the weight of the edge.

Use `startJunction` and `endJunction` to find the two graph nodes that a spline connects.

