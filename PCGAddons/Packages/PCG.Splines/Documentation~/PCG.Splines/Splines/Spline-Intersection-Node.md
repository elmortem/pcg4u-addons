# SplineIntersectionNode

Finds junctions of a spline network in the XZ plane.
Curves are subdivided adaptively so the geometric error of every junction stays within Intersection Tolerance, independent of scale or curvature. Splines are treated in world space.

The node emits two outputs: a first-class Topology (junctions with valency and the exact cut records that created them) for topology-aware nodes such as Split Splines, and a plain point list for preview and generic point nodes.

## Inputs

### Splines

World-space splines forming the network. Multiple connections are flattened into a single stable order that defines the spline index carried by each cut.

### Intersection Tolerance

Maximum geometric error of a junction position, in world units. Drives adaptive curve subdivision and refinement. Minimum 0.001.

### Merge Distance

Radius, in world units, within which cuts merge into a single junction. Minimum 0.001.

### Max Height Difference

Maximum height difference, in world units, allowed between two branches to form a junction. A grade-separated overpass above this threshold is not a junction. Zero or less ignores height entirely (strictly planar mode).

## Outputs

### Topology

Network topology: junctions with valency (number of unique incident branches) and the exact incident cuts, each carrying its spline index, curve index, curve parameter and distance along the spline.

### Results

Junction positions as points for preview and generic point nodes. In the preview, junction markers are sized and colored by valency (X, T and higher-order junctions are visually distinct).

## Notes

- An interior cut contributes two branches, an endpoint cut one; an X crossing has valency 4, a T junction valency 3.
- Endpoint-on-interior, X, T, Y and self-intersections all form junctions.
- Collinear overlaps longer than Merge Distance do not form a junction and raise a diagnostic warning.
- The result is deterministic across recompute, cache clear and domain reload.
