# PointData

Core data structure representing a single point with position, orientation, scale, and density.

A point cloud also holds named attribute columns next to this structure. The attribute nodes address both the fields below and the named columns with a selector string: a selector that starts with `$` is a built-in channel (`$position`, `$position.x`, `$normal`, `$angle`, `$scale`, `$density`), a selector without the prefix is a named attribute column. See [[Nodes|Nodes]] for the attribute node list.

The reserved attribute name `scale3` holds an optional non-uniform scale multiplier. The final scale is the uniform Scale value multiplied by this multiplier. When the column is absent, the multiplier is `(1, 1, 1)`.

Compatibility adapters can convert between a point cloud and the legacy point-list type. Each adapter copies the point list. A change on one side does not change the other side. The adapters discard named attributes.

The reserved attribute names `boundsExtents` and `boundsCenter` hold an oriented box on the point. The values are in the local space of the point, before the scale, the angle and the normal are applied. The [[Point Bounds Node|PCG/TransformPoints/Point-Bounds-Node]] writes these columns and the [[Prune Points Node|PCG/SelectPoints/Prune-Points-Node]] reads them. The reserved attribute name `priority` holds the prune priority; write it with the [[Set Attribute Node|PCG/Attributes/Set-Attribute-Node]].

## Variables

### Angle

Yaw angle in degrees around the normal.

### Density

Density value [0..1] used for filtering and modulation.

### Normal

Surface normal vector (up direction).

### Position

World position of the point.

### Scale

Uniform scale factor.

