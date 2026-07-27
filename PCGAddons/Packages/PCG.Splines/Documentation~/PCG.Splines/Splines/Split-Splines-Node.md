# SplitSplinesNode

Splits splines exactly at the given cuts or points, without resampling and without changing their shape.
Each affected cubic curve is divided precisely, so sampling the union of the output pieces reproduces the original geometry and tangents. Resampling remains a separate, explicit Resample Splines node downstream.

Two cut sources work together:

- Cuts (exact): topology cut records from Spline Intersection. Each cut applies only to its own spline, matched by spline index; neighboring non-incident splines are untouched.
- Points (fuzzy): arbitrary points. Every spline whose nearest point is closer than Snap Distance is cut. This mode is approximate and is the only consumer of Snap Distance.

## Inputs

### Splines

World-space splines to split. Multiple connections are flattened in the same stable order used by Spline Intersection, so exact cut indices line up.

### Cuts

Exact cut records produced by Spline Intersection. Each cut is applied at its own spline index, curve index and curve parameter.

### Points

Arbitrary points used as an approximate fuzzy cut source. A spline is cut where its nearest point is within Snap Distance.

### Snap Distance

Maximum distance, in world units, for the fuzzy point cut mode. Ignored by the exact cuts input.

## Outputs

### Results

Exact spline pieces. Every piece is open. Untouched knots keep their original position, tangents, tangent mode and tension; knots adjacent to a cut and new boundary knots are frozen so the shape is preserved exactly.

## Attributes

Each piece keeps the attribute row of the spline that it comes from. The node then writes four more attributes to each piece.

* `sourceSplineIndex` — index of the source spline in the flattened input order.
* `pieceIndex` — index of the piece in the source spline.
* `startJunction` — index of the junction at the start of the piece. The value is `-1` if there is no junction.
* `endJunction` — index of the junction at the end of the piece. The value is `-1` if there is no junction.

The junction indices refer to the Topology input. Use them to find the two junctions that a road segment connects.

## Notes

- A spline with no cuts is returned by reference, unchanged.
- Open splines drop cuts within 0.01 of the ends and merge near-duplicate cuts; closed splines are opened at the cuts, with circular de-duplication across the seam.
- Degenerate splines (one knot or near-zero length) pass through by reference; NaN or infinite cuts and points are discarded with a diagnostic.
- Embedded spline data and knot links are not transferred to the pieces; their presence raises a one-time diagnostic warning.
- The work is cancelable and the output is published atomically after full success.
